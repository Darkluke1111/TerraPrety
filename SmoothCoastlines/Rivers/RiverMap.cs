using MapLayer;
using SmoothCoastlines.LandformHeights;
using SmoothCoastLines.Noise;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.Rivers {

    public class RiverMap : MapLayerBase {

        public Dictionary<XZ, List<RiverData>> riversByContinent; //Stores the RiverData lists by the Continent's Regional Center XZ

        public static readonly XZ North = new XZ(0, -1);
        public static readonly XZ West = new XZ(-1, 0);
        public static readonly XZ East = new XZ(1, 0);
        public static readonly XZ South = new XZ(0, 1);

        internal ICoreServerAPI Sapi;
        internal IntDataMap2D coastMap;
        internal IntDataMap2D landformHeightMap;
        internal XZ currentRegion;
        internal int maxRegionSteps = 31; //Height + Width of the maximum search area in regions around the center of the continent, should always be kept odd to give a proper 'center' point
        internal int numPointsForAverageHeightOfCardinal = 3;
        internal int baseChanceAmount = 5000;

        /*NormalizedSimplexNoise noisegenX;
        NormalizedSimplexNoise noisegenY;
        VoronoiNoise voronoiNoise;
        Noise2D oceanNoise;*/
        MapLayerOceansSmooth oceanMap;
        MapLayerLandformsSmooth landformMap;

        /*WeightedNormalizedSimplexNoise heightMapNoise;
        NormalizedSimplexNoise heightmapNoisegenX;
        NormalizedSimplexNoise heightmapNoisegenY;*/

        int regionSize;
        int regionChunkSize;

        int noiseSizeOcean;
        int oceanPad;
        int oceanSize;
        float oceanScale;
        int noiseSizeLandform;
        int landformPad;
        int landformSize;

        int scale;
        int riverInnerSize;
        int riverChance;
        int minRiverOceanicity;
        int maxRiverOceanicity;
        float maxHeightForSink;
        float minRiverFlow;
        float maxRiverFlow;
        float unscaledFlowLoss;
        int riverRegionEdgeDeviation;
        int riverRegionCloseToCardinal;
        float riverWeirdnessMult;
        float riverCanEnterFlexibility;
        float chanceToFork;

        public RiverMap(long seed, int scale, ICoreServerAPI sapi, List<XZ> requireLandAt) : base(seed) {
            this.scale = scale;
            Sapi = sapi;

            var config = SmoothCoastlinesModSystem.config;
            regionSize = sapi.WorldManager.RegionSize;
            regionChunkSize = sapi.WorldManager.RegionSize / GlobalConstants.ChunkSize;
            oceanScale = config.noiseScale;
            noiseSizeOcean = sapi.WorldManager.RegionSize / TerraGenConfig.oceanMapScale;
            landformPad = TerraGenConfig.landformMapPadding;
            noiseSizeLandform = sapi.WorldManager.RegionSize / TerraGenConfig.landformMapScale;
            landformSize = noiseSizeLandform + 2 * landformPad;

            minRiverOceanicity = config.minimumRiverOceanicity;
            maxRiverOceanicity = config.maximumRiverOceanicity;
            riverChance = (int)(config.chanceForRiver * 1000);

            minRiverFlow = config.minimumRiverFlowStrength;
            maxRiverFlow = config.maximumRiverFlowStrength;
            unscaledFlowLoss = config.unscaledFlowLossPerRegion;
            maxHeightForSink = config.maxHeightForRiverSink;
            riverRegionEdgeDeviation = config.riverRegionDirectionRepetitionAllowance;
            riverRegionCloseToCardinal = config.riverRegionCloseToCardinalWidth;
            riverWeirdnessMult = config.riverWeirdnessChanceMult;
            riverCanEnterFlexibility = config.riverRegionCanEnterFlexibility;
            chanceToFork = config.chanceToFork;

            riversByContinent = new Dictionary<XZ, List<RiverData>>();
            /*voronoiNoise = new VoronoiNoise(seed + 2, oceanScale, requireLandAt);
            oceanNoise = new NoiseRemapper(voronoiNoise, config.remappingKeys, config.remappingValues);

            int woctaves = 4;
            float wscale = config.oceanWobbleScale * oceanScale;
            float wpersistence = 0.9f;
            wobbleIntensity = config.oceanWobbleIntensity * oceanScale;
            noisegenX = NormalizedSimplexNoise.FromDefaultOctaves(woctaves, 1 / wscale, wpersistence, seed + 2);
            noisegenY = NormalizedSimplexNoise.FromDefaultOctaves(woctaves, 1 / wscale, wpersistence, seed + 1231296);*/

            var genMaps = sapi.ModLoader.GetModSystem<GenMaps>();
            oceanMap = (MapLayerOceansSmooth)genMaps.oceanGen;
            landformMap = (MapLayerLandformsSmooth)genMaps.landformsGen;
        }

        public void SetMapsAndSizesFromRegion(IntDataMap2D coastMap, IntDataMap2D landformHeightMap, int riverInnerSize, int opad, XZ regionCoords) {
            this.coastMap = coastMap;
            this.landformHeightMap = landformHeightMap;
            this.riverInnerSize = riverInnerSize;
            currentRegion = regionCoords;

            oceanPad = opad;
            oceanSize = noiseSizeOcean + 2 * oceanPad;
        }

        public override int[] GenLayer(int xCoord, int zCoord, int sizeX, int sizeZ) {
            int[] result = new int[sizeX * sizeZ];
            List<XZ> continentsInfluencingRegion = new List<XZ>();

            PrepareRiversAtContinentalLevel(xCoord, zCoord, sizeX, sizeZ, ref continentsInfluencingRegion);
            //Generate this Region's required Chunk-Level parts of the rivers here!

            var conts = GetContinentsWithRiversEnteringRegion(continentsInfluencingRegion, currentRegion);

            for (int i = 0; i < result.Length; i++) {
                if (conts.Count > 0) {
                    result[i] = 1;
                } else {
                    result[i] = 0;
                }
            }

            return result;
        }

        //This will check to see where this Region lies in relation to the nearest continent, in all four corners, as each corner could lie closer to one continent over another.
        //To allow for multiple River sinks starting in both directions, the full four-way check must be done.
        //This handles figuring out which continents are part of this region, then if they need to have rivers initialized at the Region-Level.
        //Afterwards, it will return back to the GenLayer, and then for this specific Region, it's time to generate the map-chunk-level chunks of the rivers that pass through this Region.
        private void PrepareRiversAtContinentalLevel(int xCoord, int zCoord, int sizeX, int sizeZ, ref List<XZ> continentsInfluencingRegion) {
            int oceanX = currentRegion.X * noiseSizeOcean - oceanPad;
            int oceanZ = currentRegion.Z * noiseSizeOcean - oceanPad;

            XZ centerOfRegion = new(oceanX + (oceanSize / 2), oceanZ + (oceanSize / 2));
            XZ centerRegionOfCont = oceanMap.GetContinentalCenter(centerOfRegion.X, centerOfRegion.Z);
            centerRegionOfCont.X = (centerRegionOfCont.X + oceanPad) / noiseSizeOcean; //This should properly translate it back to region coordinates.
            centerRegionOfCont.Z = (centerRegionOfCont.Z + oceanPad) / noiseSizeOcean;
            Queue<XZ> continentsNeedingPrep = new Queue<XZ>();
            continentsNeedingPrep.Clear();

            if (!riversByContinent.ContainsKey(centerRegionOfCont)) {
                continentsNeedingPrep.Enqueue(centerRegionOfCont);
            }

            continentsInfluencingRegion.Add(centerRegionOfCont);

            if (continentsNeedingPrep.Count <= 0) {
                return;
            }

            while (continentsNeedingPrep.Count > 0) { //This all only needs to run once per continental region to build the actual rivers in one batch. They are needed before any chunks generate, but possibly can attempt in the future to build this off-thread and have chunk generation wait for the existance of it...? How would I do that without stalling everything should it hit a chunk needing data and lacking any.
                XZ contCoord = continentsNeedingPrep.Dequeue();
                BitArray visitedRegions = new BitArray(maxRegionSteps * maxRegionSteps);
                visitedRegions.SetAll(false);
                List<RiverData> continentalRivers = new List<RiverData>();
                riversByContinent[contCoord] = continentalRivers;

                RiverRegionStep center = new RiverRegionStep();
                center.prevOceanicity = 0;
                center.regionCoords.X = contCoord.X;
                center.regionCoords.Z = contCoord.Z;
                center.visitedCoords.X = (maxRegionSteps - 1) / 2;
                center.visitedCoords.Z = (maxRegionSteps - 1) / 2;
                List<XZ> riverStarts = new List<XZ>(); //Regional Coordinates
                List<float> riverHeightEstimates = new List<float>();

                InitPositionSeed(contCoord.X, contCoord.Z); //Init with the current Continent's X and Z probably? Can reuse this later but on the more defined coordinates for Region and Chunk ect
                RecursivelyIntializeContinents(center, ref visitedRegions, ref riverStarts, ref riverHeightEstimates);

                if (riverStarts.Count <= 0) {
                    continue;
                }

                //Later on, when parts of the River is actually generated, this means we can potentially store data in the save and restore it to expedite the river gen process any time a world or server reboots.
                for (int i = 0; i < riverStarts.Count; i++) { //Might be able to get away with not fully mapping out the chunks for EVERY river all at once... And only go as refined as the chunk level when we are attempting to build that map? Hmm...
                    var startRegion = riverStarts[i];
                    var startHeight = riverHeightEstimates[i];
                    var percentStrength = NextInt(100) / 100f;
                    var waterFlow = GameMath.Lerp(minRiverFlow, maxRiverFlow, percentStrength);
                    RiverData workingRiver = new RiverData(contCoord, startRegion, waterFlow);
                    int repeatedDirectionCount = 0; //TODO -- ACTUALLY IMPLEMENT THIS LOL <---

                    //Set up and enqueue the first region.
                    Queue<RiverRegion> activeRegionPoints = new Queue<RiverRegion>();
                    RiverRegion sinkRegion = new RiverRegion(workingRiver, startRegion, startHeight, -1);
                    workingRiver.AddRegionToRiver(startRegion, sinkRegion);
                    activeRegionPoints.Enqueue(sinkRegion);

                    //Each River needs a starting strength value, which will decrement for each region added on. Configurable range for maximum and minimum loss?
                    //For each added Region on the river, need to compute the strength loss from Downstream to Upstream, any possible forks or other special things that change the direction, and the exact world coordinates along the edge it needs to line up at. Y doesn't matter until the Chunk level, since the blocks won't actually exist until then.
                    while (activeRegionPoints.Count > 0) {
                        var curRegion = activeRegionPoints.Dequeue();
                        waterFlow -= unscaledFlowLoss;
                        if (waterFlow < 0) {
                            continue;
                        }
                        XZ centerOffset = GetRegionDirectionTo(center.regionCoords, curRegion.regionCoords); //This is the number of region steps in each direction to get to the center, kinda used as a counter for both and to help determine region weighting.

                        //Every Region added needs to calculate the flow loss for each step, then append all initializing data needed.
                        //But for all regions after the first, we need to first pick a direction to travel in towards the center...
                        // Following the same pattern of DesiredCoords - CurrentCoords, this will give the number of regions in the 2 cardinals to move to get there
                        // -Z = N, +Z = S, -X = W, +X = E
                        // This gives a relative direction and a 'slice' to kinda constrain the river inside while focusing on heading towards the center?

                        bool closeToCenter = false;
                        XZ downDir; //This direction is the direction it last came from - we do not want to backtrack, so this direction is always off limits.
                        if (curRegion.downstreamRegion != null) {
                            downDir = curRegion.GetDirectionTo(curRegion.downstreamRegion.regionCoords);
                            var downAvgHeight = GetAverageHeightMapValuesOfCardinal(curRegion.regionCoords, downDir);
                            if (downAvgHeight > curRegion.averageHeight && (downAvgHeight - curRegion.averageHeight) > riverCanEnterFlexibility) {
                                continue; //This step is to actually compare the average of this Region's core heights with the 'down' region to see if it's within specifications to attempt to process into another region, or if it should possibly end here somehow as it started to go back 'downhill' as it went to the center of this region.
                            }
                        } else {
                            downDir = SpoofPrevDir(centerOffset, ref closeToCenter);
                        }

                        //Potentionally refine things later on:
                        // Currently it does not factor in overlaps or already 'populated' regions
                        //  Could be a problem due to the weighting as is, will encourage forks to follow similar logic to the main river path and likely to overlap?
                        //  Maybe not an issue? Could have it split into two and reform but, is that realistic?
                        // Similarly does not account for collisions, but, MIGHT lead to avoiding them naturally? If spread out at least
                        // Adjust handling based on if it is a Fork or not? End it quicker?

                        RegionStepDirectionChances rngStep = new RegionStepDirectionChances(baseChanceAmount); //This initializes all chances to this baseChance value, which then gets adjusted below based on multipliers.
                        rngStep.PreventDirectionChoice(downDir); //Set the Downstream Direction as off-limits!

                        float dominantToSecondaryRatio;
                        bool XDominant; //Is the X-Offset the larger value? If so, true. If not, false.
                        if (Math.Abs(centerOffset.X) > Math.Abs(centerOffset.Z)) {
                            dominantToSecondaryRatio = (float)Math.Abs(centerOffset.X) / (float)(Math.Abs(centerOffset.X) + Math.Abs(centerOffset.Z));
                            XDominant = true;
                        } else {
                            dominantToSecondaryRatio = (float)Math.Abs(centerOffset.Z) / (float)(Math.Abs(centerOffset.X) + Math.Abs(centerOffset.Z));
                            XDominant = false;
                        }

                        if (rngStep.NorthFree) { //-Z - Is North still possible?
                            if (workingRiver.DoesRiverPassThroughRegion(new XZ(curRegion.regionCoords.X + North.X, curRegion.regionCoords.Z + North.Z))) {
                                rngStep.PreventDirectionChoice(North);
                            } else {
                                rngStep.NorthHeight = GetAverageHeightMapValuesOfCardinal(curRegion.regionCoords, North);
                                if (curRegion.averageHeight < rngStep.NorthHeight) {
                                    if (centerOffset.Z < 0) { //If the Center Offset is negative in the Z coordinate then it is towards the North. This means it is heading towards the center.
                                        if (!XDominant) { //Is the Z coord the larger one?
                                            rngStep.NorthChance = (int)(rngStep.NorthChance * dominantToSecondaryRatio);
                                        } else {
                                            rngStep.NorthChance = (int)(rngStep.NorthChance * (1 - dominantToSecondaryRatio));
                                        }
                                    } else { //Otherwise it would be a 'weird' step! Augment the chance accordingly.
                                        rngStep.NorthChance = (int)(rngStep.NorthChance * riverWeirdnessMult);
                                    }
                                    rngStep.NorthChance = (int)(rngStep.NorthChance * (1 - rngStep.NorthHeight));
                                } else {
                                    rngStep.PreventDirectionChoice(North);
                                }
                            }
                        }

                        if (rngStep.WestFree) { //-X - Is West ... ect
                            if (workingRiver.DoesRiverPassThroughRegion(new XZ(curRegion.regionCoords.X + West.X, curRegion.regionCoords.Z + West.Z))) {
                                rngStep.PreventDirectionChoice(West);
                            } else {
                                rngStep.WestHeight = GetAverageHeightMapValuesOfCardinal(curRegion.regionCoords, West);
                                if (curRegion.averageHeight < rngStep.WestHeight) {
                                    if (centerOffset.X < 0) {
                                        if (!XDominant) {
                                            rngStep.WestChance = (int)(rngStep.WestChance * dominantToSecondaryRatio);
                                        } else {
                                            rngStep.WestChance = (int)(rngStep.WestChance * (1 - dominantToSecondaryRatio));
                                        }
                                    } else {
                                        rngStep.WestChance = (int)(rngStep.WestChance * riverWeirdnessMult);
                                    }
                                    rngStep.WestChance = (int)(rngStep.WestChance * (1 - rngStep.WestHeight));
                                } else {
                                    rngStep.PreventDirectionChoice(West);
                                }
                            }
                        }

                        if (rngStep.EastFree) { //+X
                            if (workingRiver.DoesRiverPassThroughRegion(new XZ(curRegion.regionCoords.X + East.X, curRegion.regionCoords.Z + East.Z))) {
                                rngStep.PreventDirectionChoice(East);
                            } else {
                                rngStep.EastHeight = GetAverageHeightMapValuesOfCardinal(curRegion.regionCoords, East);
                                if (curRegion.averageHeight < rngStep.EastHeight) {
                                    if (centerOffset.X > 0) {
                                        if (!XDominant) {
                                            rngStep.EastChance = (int)(rngStep.EastChance * dominantToSecondaryRatio);
                                        } else {
                                            rngStep.EastChance = (int)(rngStep.EastChance * (1 - dominantToSecondaryRatio));
                                        }
                                    } else {
                                        rngStep.EastChance = (int)(rngStep.EastChance * riverWeirdnessMult);
                                    }
                                    rngStep.EastChance = (int)(rngStep.EastChance * (1 - rngStep.EastHeight));
                                } else {
                                    rngStep.PreventDirectionChoice(East);
                                }
                            }
                        }

                        if (rngStep.SouthFree) { //+Z
                            if (workingRiver.DoesRiverPassThroughRegion(new XZ(curRegion.regionCoords.X + South.X, curRegion.regionCoords.Z + South.Z))) {
                                rngStep.PreventDirectionChoice(South);
                            } else {
                                rngStep.SouthHeight = GetAverageHeightMapValuesOfCardinal(curRegion.regionCoords, South);
                                if (curRegion.averageHeight < rngStep.SouthHeight) {
                                    if (centerOffset.Z > 0) {
                                        if (!XDominant) {
                                            rngStep.SouthChance = (int)(rngStep.SouthChance * dominantToSecondaryRatio);
                                        } else {
                                            rngStep.SouthChance = (int)(rngStep.SouthChance * (1 - dominantToSecondaryRatio));
                                        }
                                    } else {
                                        rngStep.SouthChance = (int)(rngStep.SouthChance * riverWeirdnessMult);
                                    }
                                    rngStep.SouthChance = (int)(rngStep.SouthChance * (1 - rngStep.SouthHeight));
                                } else {
                                    rngStep.PreventDirectionChoice(South);
                                }
                            }
                        }

                        //Thought process behind this is to try and combine all of the factors effecting the River's possible direction...
                        //  The tendency to always head towards the center of the continent is the primary factor
                        //  Then also factor in the height - as long as that direction is higher then the current region's average
                        //  Sum this all together and it should give us a value to get the nextInt from 0 to this number, and similar to how Landform Weighting works, this weights and chooses the direction.

                        var fullChance = rngStep.GetFullChance();
                        if (fullChance <= 0) {
                            //If it's zero (or somehow less), the River has concluded here. There's no more valid places for it to go without it being weird - No need to add on anything else. Perhaps this might be a way to add in some special River effects to let it continue on regardless?
                            continue;
                        }

                        XZ primaryChoiceDir = new XZ(-1, -1);
                        float primaryChoiceHeight = 0f;
                        var rngPrimaryChance = NextInt(fullChance);

                        if (rngStep.NorthFree && rngPrimaryChance < rngStep.NorthChance) {
                            rngStep.PreventDirectionChoice(North);
                            primaryChoiceDir = North;
                        } else if (rngStep.EastFree && rngPrimaryChance < (rngStep.NorthChance + rngStep.EastChance)) {
                            rngStep.PreventDirectionChoice(East);
                            primaryChoiceDir = East;
                        } else if (rngStep.SouthFree && rngPrimaryChance < (rngStep.NorthChance + rngStep.EastChance + rngStep.SouthChance)) {
                            rngStep.PreventDirectionChoice(South);
                            primaryChoiceDir = South;
                        } else if (rngStep.WestFree) {
                            rngStep.PreventDirectionChoice(West);
                            primaryChoiceDir = West;
                        } else {
                            continue;
                        }
                        primaryChoiceHeight = GetAverageHeightForRegion(new XZ(curRegion.regionCoords.X + primaryChoiceDir.X, curRegion.regionCoords.Z + primaryChoiceDir.Z));

                        //Where would be best to handle a possible Fork? Should be queued BEFORE the main route is queued to ensure it is added in the right order.
                        fullChance = rngStep.GetFullChance();
                        if (fullChance > 0) {
                            var doFork = NextInt(100);
                            if (doFork < (chanceToFork * 100)) {
                                XZ forkDir = new XZ(-2, -2);
                                float forkHeight = 0f;
                                var rngForkChance = NextInt(fullChance);

                                if (rngStep.NorthFree && rngForkChance < rngStep.NorthChance) {
                                    rngStep.PreventDirectionChoice(North);
                                    forkDir = North;
                                } else if (rngStep.EastFree && rngForkChance < (rngStep.NorthChance + rngStep.EastChance)) {
                                    rngStep.PreventDirectionChoice(East);
                                    forkDir = East;
                                } else if (rngStep.SouthFree && rngForkChance < (rngStep.NorthChance + rngStep.EastChance + rngStep.SouthChance)) {
                                    rngStep.PreventDirectionChoice(South);
                                    forkDir = South;
                                } else if (rngStep.WestFree) {
                                    rngStep.PreventDirectionChoice(West);
                                    forkDir = West;
                                }

                                if (forkDir.X != -2) {
                                    forkHeight = GetAverageHeightForRegion(new XZ(curRegion.regionCoords.X + forkDir.X, curRegion.regionCoords.Z + forkDir.Z));
                                    
                                    //To initialize a Fork, do not send it a DownRegion on creation, instead just call the AttachForkRegion on the Downstream end afterwards.
                                    RiverRegion forkedRegion = new RiverRegion(workingRiver, new XZ(curRegion.regionCoords.X + forkDir.X, curRegion.regionCoords.Z + forkDir.Z), forkHeight, waterFlow);
                                    XZ forkConnectionPos = PickConnectingWorldCoords(curRegion, forkDir);
                                    if (forkConnectionPos.X == -1) {
                                        SmoothCoastlinesModSystem.Logger.Warning("Failed to find a RiverMap Tile that fits for passing upstream into this fork region. Canceling the Fork! If this is commonplace, perhaps consider some method of continuing it here, somehow?");
                                    } else {
                                        curRegion.AttachForkRegion(forkedRegion, forkConnectionPos);
                                        workingRiver.AddRegionToRiver(forkedRegion.regionCoords, forkedRegion);
                                        activeRegionPoints.Enqueue(forkedRegion);
                                    }
                                    //Perhaps consider adding Tags to the fork region to mark it as a Fork? For refining later the generation perhaps.
                                }
                            }
                        }

                        //And now handle the primary Upstream portion of the River here!
                        curRegion.upstreamWorldPos = PickConnectingWorldCoords(curRegion, primaryChoiceDir);
                        if (curRegion.upstreamWorldPos.X == -1) {
                            SmoothCoastlinesModSystem.Logger.Warning("Failed to find a RiverMap Tile that fits for passing upstream into this next region. Ending the river in this current region instead. If this is commonplace, perhaps consider some method of continuing it here, somehow?");
                            continue;
                        }
                        RiverRegion upstreamRegion = new RiverRegion(workingRiver, new XZ(curRegion.regionCoords.X + primaryChoiceDir.X, curRegion.regionCoords.Z + primaryChoiceDir.Z), primaryChoiceHeight, waterFlow, curRegion);
                        workingRiver.AddRegionToRiver(upstreamRegion.regionCoords, upstreamRegion);
                        activeRegionPoints.Enqueue(upstreamRegion);
                    }

                    continentalRivers.Add(workingRiver);
                }
            }
        }

        private void RecursivelyIntializeContinents(RiverRegionStep curRegion, ref BitArray visitedRegions, ref List<XZ> riverStarts, ref List<float> riverHeightEstimates) { //Lets try to just poll the center of the region for it's Oceanicity and see if that's sufficient
            visitedRegions[curRegion.visitedCoords.Z * maxRegionSteps + curRegion.visitedCoords.X] = true;
            int centerOceanRegionX = (curRegion.regionCoords.X * noiseSizeOcean - oceanPad) + (oceanSize / 2); //Will this need to be a few checks? Like 4 points or something maybe for a finer detail.
            int centerOceanRegionZ = (curRegion.regionCoords.Z * noiseSizeOcean - oceanPad) + (oceanSize / 2);

            var oceanicity = oceanMap.GetOceanicityAt(centerOceanRegionX, centerOceanRegionZ);

            if (oceanicity >= minRiverOceanicity && oceanicity <= maxRiverOceanicity) {
                var avgHeight = GetAverageHeightForRegion(curRegion.regionCoords);
                if (avgHeight <= maxHeightForSink && NextInt(1000) < riverChance) {
                    riverStarts.Add(curRegion.regionCoords);
                    riverHeightEstimates.Add((float)avgHeight);
                }
            } else if (oceanicity > maxRiverOceanicity || oceanicity < curRegion.prevOceanicity) {
                return; //If it's over the max or it goes below the previous examined Oceanicity, just return since it's either going into the deep ocean where we don't want rivers, or it's a place where two continents are close enough together that it's going back away from the ocean
            }

            //Now step one region over in each direction... Up -Z
            if ((curRegion.visitedCoords.Z - 1) >= 0 && !visitedRegions[(curRegion.visitedCoords.Z - 1) * maxRegionSteps + curRegion.visitedCoords.X]) { //If in this direction, it is within bounds, and has not been visited, go in this direction as well
                RiverRegionStep upStep = new RiverRegionStep {
                    prevOceanicity = (int)oceanicity,
                    regionCoords = new XZ(curRegion.regionCoords.X, curRegion.regionCoords.Z - 1),
                    visitedCoords = new XZ(curRegion.visitedCoords.X, curRegion.visitedCoords.Z - 1)
                };
                RecursivelyIntializeContinents(upStep, ref visitedRegions, ref riverStarts, ref riverHeightEstimates);
            }

            //Right +X
            if ((curRegion.visitedCoords.X + 1) < maxRegionSteps && !visitedRegions[curRegion.visitedCoords.Z * maxRegionSteps + (curRegion.visitedCoords.X + 1)]) {
                RiverRegionStep rightStep = new RiverRegionStep {
                    prevOceanicity = (int)oceanicity,
                    regionCoords = new XZ(curRegion.regionCoords.X + 1, curRegion.regionCoords.Z),
                    visitedCoords = new XZ(curRegion.visitedCoords.X + 1, curRegion.visitedCoords.Z)
                };
                RecursivelyIntializeContinents(rightStep, ref visitedRegions, ref riverStarts, ref riverHeightEstimates);
            }

            //Down +Z
            if ((curRegion.visitedCoords.Z + 1) < maxRegionSteps && !visitedRegions[(curRegion.visitedCoords.Z + 1) * maxRegionSteps + curRegion.visitedCoords.X]) {
                RiverRegionStep downStep = new RiverRegionStep {
                    prevOceanicity = (int)oceanicity,
                    regionCoords = new XZ(curRegion.regionCoords.X, curRegion.regionCoords.Z + 1),
                    visitedCoords = new XZ(curRegion.visitedCoords.X, curRegion.visitedCoords.Z + 1)
                };
                RecursivelyIntializeContinents(downStep, ref visitedRegions, ref riverStarts, ref riverHeightEstimates);
            }

            //Left -X
            if ((curRegion.visitedCoords.X - 1) >= 0 && !visitedRegions[curRegion.visitedCoords.Z * maxRegionSteps + (curRegion.visitedCoords.X - 1)]) {
                RiverRegionStep leftStep = new RiverRegionStep {
                    prevOceanicity = (int)oceanicity,
                    regionCoords = new XZ(curRegion.regionCoords.X - 1, curRegion.regionCoords.Z),
                    visitedCoords = new XZ(curRegion.visitedCoords.X - 1, curRegion.visitedCoords.Z)
                };
                RecursivelyIntializeContinents(leftStep, ref visitedRegions, ref riverStarts, ref riverHeightEstimates);
            }
        }

        //If this is the second step after the sinkRegion, it won't have a downstream region. Need to just 'spoof' the downDir to be whichever way would be the worst to go.
        private XZ SpoofPrevDir(XZ centerOffset, ref bool closeToCenter) {
            XZ downDir = new XZ();
            int dirX = 0; //If this direction is perfectly in line with the center, it will remain 0.
            if (centerOffset.X < 0) { //West
                dirX = -1;
            } else if (centerOffset.X > 0) { //East
                dirX = 1;
            }

            int dirZ = 0; //Same as above for Z! Only time both will be 0 is when it is the Center region.
            if (centerOffset.Z < 0) { //North
                dirZ = -1;
            } else if (centerOffset.Z > 0) { //South
                dirZ = 1;
            }

            bool smallX = false;
            if (centerOffset.X < riverRegionCloseToCardinal && centerOffset.X > -riverRegionCloseToCardinal) {
                smallX = true;
            }

            bool smallZ = false;
            if (centerOffset.Z < riverRegionCloseToCardinal && centerOffset.Z > -riverRegionCloseToCardinal) {
                smallZ = true;
            }

            if (smallX && smallZ) {
                closeToCenter = true;
                downDir.X = -2;
                downDir.Z = -2; //Set something just in case so it isn't null, but a -2 should be checked for in this case and handled like a 'nothing' value.
            } else {
                if (centerOffset.X < centerOffset.Z) { //Technically weighted towards X because it's looking for less than, and not equal, but eeeh. It's the first step is all.
                    downDir.X = 0;
                    downDir.Z = -dirZ;
                } else {
                    downDir.X = -dirX;
                    downDir.Z = 0;
                }
            }

            return downDir;
        }

        private float GetAverageHeightForRegion(XZ regionCoords) {
            int landformSizeFourth = landformSize / 4;
            int upLeftHeightX = (regionCoords.X * noiseSizeLandform - landformPad) + landformSizeFourth; //The upper left quadrant's centermost tile, this plus the other 3 divided by 4 can be the quick heightmap estimate?
            int upLeftHeightZ = (regionCoords.Z * noiseSizeLandform - landformPad) + landformSizeFourth;

            var avgHeight = landformMap.GetHeightMapAt(upLeftHeightX, upLeftHeightZ);
            avgHeight += landformMap.GetHeightMapAt(upLeftHeightX + (2 * landformSizeFourth), upLeftHeightZ);
            avgHeight += landformMap.GetHeightMapAt(upLeftHeightX, upLeftHeightZ + (2 * landformSizeFourth));
            avgHeight += landformMap.GetHeightMapAt(upLeftHeightX + (2 * landformSizeFourth), upLeftHeightZ + (2 * landformSizeFourth));
            avgHeight /= 4;

            return avgHeight;
        }

        //For reference: North -Z, South +Z, West -X, and East +X
        private float GetAverageHeightMapValuesOfCardinal(XZ regionCoords, XZ cardinalDir) {
            var landformOriginRegionX = regionCoords.X * noiseSizeLandform - landformPad;
            var landformOriginRegionZ = regionCoords.Z * noiseSizeLandform - landformPad;
            float averageHeight = 0f;
            //XZ coordsStep = new XZ(landformOriginRegionX, landformOriginRegionZ);
            bool XorZ; //False is X, True is Z! For which needs to be adjusted to ensure we are taking points along that cardinal edge.
            
            if (cardinalDir.X != 0) {
                XorZ = true;
                if (cardinalDir.X > 0) {
                    landformOriginRegionX += landformSize - 1;
                }
            } else {
                XorZ = false;
                if (cardinalDir.Z > 0) {
                    landformOriginRegionZ += landformSize - 1;
                }
            }

            int sizeFract = landformSize / (numPointsForAverageHeightOfCardinal + 1);
            for (int i = 0; i < numPointsForAverageHeightOfCardinal; i++) {
                if (XorZ) {
                    landformOriginRegionZ += sizeFract;
                } else {
                    landformOriginRegionX += sizeFract;
                }
                averageHeight += landformMap.GetHeightMapAt(landformOriginRegionX, landformOriginRegionZ);
            }

            return averageHeight / numPointsForAverageHeightOfCardinal;
        }

        //Very similar to the above, except this is to first check each tile on the RiverMap for this region and compare it's heightmap values to eliminate any that are lower then the average, and then weight towards a smoother incline.
        private XZ PickConnectingWorldCoords(RiverRegion downRegion, XZ cardinalDir) {
            var regionAvgHeight = downRegion.averageHeight;
            var landformOriginRegionX = downRegion.regionCoords.X * noiseSizeLandform - landformPad;
            var landformOriginRegionZ = downRegion.regionCoords.Z * noiseSizeLandform - landformPad;
            bool XorZ; //False is X, True is Z! For which needs to be adjusted to ensure we are taking points along that cardinal edge.

            if (cardinalDir.X != 0) {
                XorZ = true;
                if (cardinalDir.X > 0) {
                    landformOriginRegionX += landformSize - 1;
                }
            } else {
                XorZ = false;
                if (cardinalDir.Z > 0) {
                    landformOriginRegionZ += landformSize - 1;
                }
            }

            Dictionary<XZ, int> tileWeights = new Dictionary<XZ, int>();
            var maxTileWeight = 1000;
            int totalWeight = 0;
            for (int i = 0; i < landformSize; i++) {
                if (XorZ) {
                    landformOriginRegionZ += 1;
                } else {
                    landformOriginRegionX += 1;
                }

                var height = landformMap.GetHeightMapAt(landformOriginRegionX, landformOriginRegionZ);
                if (height >= downRegion.averageHeight) {
                    int weight = (int)(height * maxTileWeight);
                    tileWeights[new XZ(landformOriginRegionX, landformOriginRegionZ)] = weight;
                    totalWeight += weight;
                }
            }

            int rngFactor = NextInt(totalWeight);
            foreach (var tile in tileWeights) {
                rngFactor -= tile.Value;
                if (rngFactor < 0) {
                    XZ chosenTile = tile.Key;

                    float tileRegionX = (chosenTile.X + landformPad) / (float)noiseSizeLandform;
                    float tileRegionZ = (chosenTile.Z + landformPad) / (float)noiseSizeLandform;

                    float tileChunkX = tileRegionX * regionChunkSize;
                    float tileChunkZ = tileRegionZ * regionChunkSize;

                    return new XZ((int)(tileChunkX * GlobalConstants.ChunkSize), (int)(tileChunkZ * GlobalConstants.ChunkSize));
                }
            }

            return new XZ(-1, -1);
        }

        private List<XZ> GetContinentsWithRiversEnteringRegion(List<XZ> contCoords, XZ regionCoords) {
            var contList = new List<XZ>();

            foreach (var cont in contCoords) {
                if (riversByContinent.ContainsKey(cont) && !contList.Contains(cont)) {
                    var list = riversByContinent[cont];
                    foreach (var river in list) {
                        if (river.DoesRiverPassThroughRegion(regionCoords)) {
                            contList.Add(cont);
                            break;
                        }
                    }
                }
            }

            return contList;
        }

        public static XZ GetRegionDirectionTo(XZ desiredRegion, XZ currentRegion) {
            XZ direction = new XZ();
            direction.X = desiredRegion.X - currentRegion.X;
            direction.Z = desiredRegion.Z - currentRegion.Z;
            return direction;
        }

        struct RiverRegionStep {
            public int prevOceanicity;
            public int heightmapQuickAverage;
            public XZ regionCoords;
            public XZ visitedCoords;
        }
    }
}
