using SmoothCoastlines.LandformHeights;
using SmoothCoastLines.Noise;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.Rivers {

    public class RiverMap : MapLayerBase {

        public Dictionary<XZ, List<RiverData>> riversByContinent;

        public static readonly XZ North = new XZ(0, -1);
        public static readonly XZ West = new XZ(-1, 0);
        public static readonly XZ East = new XZ(1, 0);
        public static readonly XZ South = new XZ(0, 1);

        internal ICoreServerAPI Sapi;
        internal IntDataMap2D coastMap;
        internal IntDataMap2D landformHeightMap;
        internal XZ currentRegion;
        internal int maxRegionSteps = 31; //Height + Width of the maximum search area in regions around the center of the continent, should always be kept odd to give a proper 'center' point

        NormalizedSimplexNoise noisegenX;
        NormalizedSimplexNoise noisegenY;
        VoronoiNoise voronoiNoise;
        Noise2D oceanNoise;

        WeightedNormalizedSimplexNoise heightMapNoise;
        NormalizedSimplexNoise heightmapNoisegenX;
        NormalizedSimplexNoise heightmapNoisegenY;

        int regionSize;

        int noiseSizeOcean;
        int oceanPad;
        int oceanSize;
        float oceanScale;
        float wobbleIntensity;

        int scale;
        int riverInnerSize;
        int riverChance;
        int minRiverOceanicity;
        int maxRiverOceanicity;
        float minRiverFlow;
        float maxRiverFlow;
        float unscaledFlowLoss;
        int riverRegionEdgeDeviation;
        int riverRegionCloseToCardinal;
        float riverWeirdness;

        public RiverMap(long seed, int scale, ICoreServerAPI sapi, List<XZ> requireLandAt) : base(seed) {
            this.scale = scale;
            Sapi = sapi;

            var config = SmoothCoastlinesModSystem.config;
            regionSize = sapi.World.BlockAccessor.RegionSize;
            oceanScale = config.noiseScale;
            noiseSizeOcean = sapi.WorldManager.RegionSize / TerraGenConfig.oceanMapScale;
            minRiverOceanicity = config.minimumRiverOceanicity;
            maxRiverOceanicity = config.maximumRiverOceanicity;
            riverChance = (int)(config.chanceForRiver * 1000);
            minRiverFlow = config.minimumRiverFlowStrength;
            maxRiverFlow = config.maximumRiverFlowStrength;
            unscaledFlowLoss = config.unscaledFlowLossPerRegion;
            riverRegionEdgeDeviation = config.riverRegionDirectionRepetitionAllowance;
            riverRegionCloseToCardinal = config.riverRegionCloseToCardinalWidth;
            riverWeirdness = config.riverWeirdnessChance;

            riversByContinent = new Dictionary<XZ, List<RiverData>>();
            voronoiNoise = new VoronoiNoise(seed + 2, oceanScale, requireLandAt);
            oceanNoise = new NoiseRemapper(voronoiNoise, config.remappingKeys, config.remappingValues);

            int woctaves = 4;
            float wscale = config.oceanWobbleScale * oceanScale;
            float wpersistence = 0.9f;
            wobbleIntensity = config.oceanWobbleIntensity * oceanScale;
            noisegenX = NormalizedSimplexNoise.FromDefaultOctaves(woctaves, 1 / wscale, wpersistence, seed + 2);
            noisegenY = NormalizedSimplexNoise.FromDefaultOctaves(woctaves, 1 / wscale, wpersistence, seed + 1231296);

            ((MapLayerLandformsSmooth)sapi.ModLoader.GetModSystem<GenMaps>().landformsGen).BorrowHeightMapReference(ref heightMapNoise, ref heightmapNoisegenX, ref heightmapNoisegenY);
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

            for (int i = 0; i < result.Length; i++) {
                result[i] = 0;
            }

            return result;
        }

        //This will check to see where this Region lies in relation to the nearest continent, in all four corners, as each corner could lie closer to one continent over another.
        //To allow for multiple River sinks starting in both directions, the full four-way check must be done.
        public void PrepareRiversAtContinentalLevel(int xCoord, int zCoord, int sizeX, int sizeZ, ref List<XZ> continentsInfluencingRegion) {
            int oceanX = currentRegion.X * noiseSizeOcean - oceanPad;
            int oceanZ = currentRegion.Z * noiseSizeOcean - oceanPad;

            XZ upperLeft = new(oceanX, oceanZ);
            XZ upperRight = new(oceanX + (oceanSize - 1), oceanZ);
            XZ lowerLeft = new(oceanX, oceanZ + (oceanSize - 1));
            XZ lowerRight = new(oceanX + (oceanSize - 1), oceanZ + (oceanSize - 1));
            XZ upLCont = new(((int)(upperLeft.X / oceanScale)), ((int)(upperLeft.Z / oceanScale)));
            XZ upRCont = new(((int)(upperRight.X / oceanScale)), ((int)(upperRight.Z / oceanScale)));
            XZ lowLCont = new(((int)(lowerLeft.X / oceanScale)), ((int)(lowerLeft.Z / oceanScale)));
            XZ lowRCont = new(((int)(lowerRight.X / oceanScale)), ((int)(lowerRight.Z / oceanScale)));
            Queue<XZ> continentsNeedingPrep = new Queue<XZ>();
            continentsNeedingPrep.Clear();

            if (!riversByContinent.ContainsKey(upLCont) && !continentsNeedingPrep.Contains(upLCont)) {
                continentsNeedingPrep.Enqueue(upLCont);
            }
            if (!riversByContinent.ContainsKey(upRCont) && !continentsNeedingPrep.Contains(upRCont)) {
                continentsNeedingPrep.Enqueue(upRCont);
            }
            if (!riversByContinent.ContainsKey(lowLCont) && !continentsNeedingPrep.Contains(lowLCont)) {
                continentsNeedingPrep.Enqueue(lowLCont);
            }
            if (!riversByContinent.ContainsKey(lowRCont) && !continentsNeedingPrep.Contains(lowRCont)) {
                continentsNeedingPrep.Enqueue(lowRCont);
            }

            continentsInfluencingRegion.Add(upLCont);
            if (!continentsInfluencingRegion.Contains(upRCont)) {
                continentsInfluencingRegion.Add(upRCont);
            }
            if (!continentsInfluencingRegion.Contains(lowLCont)) {
                continentsInfluencingRegion.Add(lowLCont);
            }
            if (!continentsInfluencingRegion.Contains(lowRCont)) {
                continentsInfluencingRegion.Add(lowRCont);
            }

            if (continentsNeedingPrep.Count <= 0) {
                return;
            }

            while (continentsNeedingPrep.Count > 0) { //This all only needs to run once per continental region to build the actual rivers in one batch. They are needed before any chunks generate, but possibly can attempt in the future to build this off-thread and have chunk generation wait for the existance of it...? How would I do that without stalling everything should it hit a chunk needing data and lacking any.
                XZ continentCoord = continentsNeedingPrep.Dequeue();
                BitArray visitedRegions = new BitArray(maxRegionSteps * maxRegionSteps);
                visitedRegions.SetAll(false);
                List<RiverData> continentalRivers = new List<RiverData>();
                riversByContinent[continentCoord] = continentalRivers;

                RiverRegionStep center = new RiverRegionStep();
                center.prevOceanicity = 0;
                center.regionCoords.X = (((int)(continentCoord.X * oceanScale)) + oceanPad) / noiseSizeOcean;
                center.regionCoords.Z = (((int)(continentCoord.Z * oceanScale)) + oceanPad) / noiseSizeOcean;
                center.visitedCoords.X = (maxRegionSteps - 1) / 2;
                center.visitedCoords.Z = (maxRegionSteps - 1) / 2;
                List<XZ> riverStarts = new List<XZ>();

                InitPositionSeed(continentCoord.X, continentCoord.Z); //Init with the current Continent's X and Z probably? Can reuse this later but on the more defined coordinates for Region and Chunk ect
                RecursivelyIntializeContinents(center, ref visitedRegions, ref riverStarts);

                if (riverStarts.Count <= 0) {
                    continue;
                }

                //Later on, when parts of the River is actually generated, this means we can potentially store data in the save and restore it to expedite the river gen process any time a world or server reboots.
                for (int i = 0; i < riverStarts.Count; i++) { //Might be able to get away with not fully mapping out the chunks for EVERY river all at once... And only go as refined as the chunk level when we are attempting to build that map? Hmm...
                    var startRegion = riverStarts[i];
                    var percentStrength = NextInt(100) / 100f;
                    var waterFlow = GameMath.Lerp(minRiverFlow, maxRiverFlow, percentStrength);
                    RiverData workingRiver = new RiverData(continentCoord, startRegion, waterFlow);
                    RiverRegion prevRegion = null;
                    float prevFlow = -1;
                    XZ centerOffset = GetRegionDirectionTo(center.regionCoords, startRegion); //This is the number of region steps in each direction to get to the center, kinda used as a counter for both and to help determine region weighting.
                    int repeatedDirectionCount = 0;

                    //Each River needs a starting strength value, which will decrement for each region added on. Configurable range for maximum and minimum loss?
                    //For each added Region on the river, need to compute the strength loss from Downstream to Upstream, any possible forks or other special things that change the direction, and the exact world coordinates along the edge it needs to line up at. Y doesn't matter until the Chunk level, since the blocks won't actually exist until then.
                    while (waterFlow > 0) {
                        //Every Region added needs to calculate the flow loss for each step, then append all initializing data needed.
                        if (prevRegion == null) { //If the prevRegion is null, that means this is the first loop through.
                            //It just needs to set up the first Region.
                            RiverRegion sinkRegion = new RiverRegion(workingRiver, startRegion, prevFlow, waterFlow);
                            prevFlow = waterFlow;
                            waterFlow -= unscaledFlowLoss;
                            prevRegion = sinkRegion;
                            continue;
                        }

                        //But for all regions after the first, we need to first pick a direction to travel in towards the center...
                        // Following the same pattern of DesiredCoords - CurrentCoords, this will give the number of regions in the 2 cardinals to move to get there
                        // -Z = N, +Z = S, -X = W, +X = E
                        // This gives a relative direction and a 'slice' to kinda constrain the river inside while focusing on heading towards the center?

                        bool closeToCenter = false;
                        XZ downDir; //This direction is the direction it last came from - we do not want to backtrack, so this direction is always off limits.
                        if (prevRegion.downstreamRegion != null) {
                            downDir = prevRegion.GetDirectionTo(prevRegion.downstreamRegion.regionCoords);
                        } else {
                            downDir = SpoofPrevDir(centerOffset, ref closeToCenter);
                        }

                        BitArray dirsUsed = new BitArray(4); //This just represents what directions have been chosen already, to ideally make the final check quicker if needed. Array index 0 = N, 1 = S, 2 = W, 3 = E
                        dirsUsed.SetAll(false);
                        XZ dominantStepDir = new XZ();
                        float dominantStepChance;
                        XZ secondaryStepDir = new XZ();
                        float secondaryStepChance;
                        XZ weirdStepDir = new XZ();
                        float weirdStepChance;

                        if (Math.Abs(centerOffset.X) > Math.Abs(centerOffset.Z)) {
                            if (centerOffset.X < 0) {
                                dominantStepDir = West;
                                dirsUsed[2] = true;
                            } else {
                                dominantStepDir = East;
                                dirsUsed[3] = true;
                            }

                            if (centerOffset.Z < 0) {
                                secondaryStepDir = North;
                                dirsUsed[0] = true;
                            } else {
                                secondaryStepDir = South;
                                dirsUsed[1] = true;
                            }
                        } else {
                            if (centerOffset.Z < 0) {
                                dominantStepDir = North;
                                dirsUsed[0] = true;
                            } else {
                                dominantStepDir = South;
                                dirsUsed[1] = true;
                            }

                            if (centerOffset.X < 0) {
                                secondaryStepDir = West;
                                dirsUsed[2] = true;
                            } else {
                                secondaryStepDir = East;
                                dirsUsed[3] = true;
                            }
                        }

                        if (!closeToCenter) {
                            if (downDir.X != 0) {
                                if (downDir.X < 0) {
                                    dirsUsed[2] = true;
                                } else {
                                    dirsUsed[3] = true;
                                }
                            } else {
                                if (downDir.Z < 0) {
                                    dirsUsed[0] = true;
                                } else {
                                    dirsUsed[1] = true;
                                }
                            }

                            for (int j = 0; j < dirsUsed.Length; j++) {
                                if (!dirsUsed[j]) {
                                    switch (j) {
                                        case 0:
                                            weirdStepDir = North;
                                            break;
                                        case 1:
                                            weirdStepDir = South;
                                            break;
                                        case 2:
                                            weirdStepDir = West;
                                            break;
                                        case 3:
                                            weirdStepDir = East;
                                            break;
                                    }

                                }
                            }
                            weirdStepChance = riverWeirdness;
                        } else {
                            weirdStepChance = 0;
                        }


                    }
                }
            }
            

        }

        private void RecursivelyIntializeContinents(RiverRegionStep curRegion, ref BitArray visitedRegions, ref List<XZ> riverStarts) { //Lets try to just poll the center of the region for it's Oceanicity and see if that's sufficient
            visitedRegions[curRegion.visitedCoords.Z * maxRegionSteps + curRegion.visitedCoords.X] = true;
            int centerOceanRegionX = (curRegion.regionCoords.X * noiseSizeOcean - oceanPad) + (oceanSize / 2); //Will this need to be a few checks? Like 4 points or something maybe for a finer detail.
            int centerOceanRegionZ = (curRegion.regionCoords.Z * noiseSizeOcean - oceanPad) + (oceanSize / 2);

            var undestortedNoise = voronoiNoise.getValueAt(centerOceanRegionX, centerOceanRegionZ);
            var offsetX = (int)(wobbleIntensity * noisegenX.Noise(centerOceanRegionX, centerOceanRegionZ) * undestortedNoise);
            var offsetZ = (int)(wobbleIntensity * noisegenY.Noise(centerOceanRegionX, centerOceanRegionZ) * undestortedNoise);
            var unscaledXpos = centerOceanRegionX + offsetX;
            var unscaledZpos = centerOceanRegionZ + offsetZ;
            var oceanicity = oceanNoise.getValueAt(unscaledXpos, unscaledZpos) * 255;

            if (oceanicity >= minRiverOceanicity && oceanicity <= maxRiverOceanicity) {
                if (NextInt(1000) < riverChance) {
                    riverStarts.Add(curRegion.regionCoords);
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
                RecursivelyIntializeContinents(upStep, ref visitedRegions, ref riverStarts);
            }

            //Right +X
            if ((curRegion.visitedCoords.X + 1) < maxRegionSteps && !visitedRegions[curRegion.visitedCoords.Z * maxRegionSteps + (curRegion.visitedCoords.X + 1)]) {
                RiverRegionStep rightStep = new RiverRegionStep {
                    prevOceanicity = (int)oceanicity,
                    regionCoords = new XZ(curRegion.regionCoords.X + 1, curRegion.regionCoords.Z),
                    visitedCoords = new XZ(curRegion.visitedCoords.X + 1, curRegion.visitedCoords.Z)
                };
                RecursivelyIntializeContinents(rightStep, ref visitedRegions, ref riverStarts);
            }

            //Down +Z
            if ((curRegion.visitedCoords.Z + 1) < maxRegionSteps && !visitedRegions[(curRegion.visitedCoords.Z + 1) * maxRegionSteps + curRegion.visitedCoords.X]) {
                RiverRegionStep downStep = new RiverRegionStep {
                    prevOceanicity = (int)oceanicity,
                    regionCoords = new XZ(curRegion.regionCoords.X, curRegion.regionCoords.Z + 1),
                    visitedCoords = new XZ(curRegion.visitedCoords.X, curRegion.visitedCoords.Z + 1)
                };
                RecursivelyIntializeContinents(downStep, ref visitedRegions, ref riverStarts);
            }

            //Left -X
            if ((curRegion.visitedCoords.X - 1) >= 0 && !visitedRegions[curRegion.visitedCoords.Z * maxRegionSteps + (curRegion.visitedCoords.X - 1)]) {
                RiverRegionStep leftStep = new RiverRegionStep {
                    prevOceanicity = (int)oceanicity,
                    regionCoords = new XZ(curRegion.regionCoords.X - 1, curRegion.regionCoords.Z),
                    visitedCoords = new XZ(curRegion.visitedCoords.X - 1, curRegion.visitedCoords.Z)
                };
                RecursivelyIntializeContinents(leftStep, ref visitedRegions, ref riverStarts);
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

        public static XZ GetRegionDirectionTo(XZ desiredRegion, XZ currentRegion) {
            XZ direction = new XZ();
            direction.X = desiredRegion.X - currentRegion.X;
            direction.Z = desiredRegion.Z - currentRegion.Z;
            return direction;
        }

        struct RiverRegionStep {
            public int prevOceanicity;
            public XZ regionCoords;
            public XZ visitedCoords;
        }
    }
}
