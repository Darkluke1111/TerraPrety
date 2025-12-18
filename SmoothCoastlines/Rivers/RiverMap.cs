using Cairo;
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
using Vintagestory.API.Common;
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
        internal Dictionary<XZ, int[]> heightEstimatesByRegion;

        public static readonly XZ NorthWest = new XZ(-1, -1);
        public static readonly XZ North = new XZ(0, -1);
        public static readonly XZ NorthEast = new XZ(1, -1);
        public static readonly XZ West = new XZ(-1, 0);
        public static readonly XZ East = new XZ(1, 0);
        public static readonly XZ SouthWest = new XZ(-1, 1);
        public static readonly XZ South = new XZ(0, 1);
        public static readonly XZ SouthEast = new XZ(1, 1);
        public const float blocksPerFlowHundreth = 0.5f; //Comes out to the max and min 'flow' values equate to 75 blocks wide at maximum, and then 12.5 blocks wide at minimum (round down always?)... Potentially works? Maybe tone it down later?

        internal ICoreServerAPI Sapi;
        internal IntDataMap2D regionalCoastMap;
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
        CoastMap coastMap;

        /*WeightedNormalizedSimplexNoise heightMapNoise;
        NormalizedSimplexNoise heightmapNoisegenX;
        NormalizedSimplexNoise heightmapNoisegenY;*/

        int regionSize; //Size, in blocks, of a Region
        int regionChunkSize; //Number of Chunks in a Region
        int sealevelY;

        int noiseSizeOcean;
        int oceanPad;
        int oceanSize;
        float oceanScale;
        int numBlocksInOceanMapTile;
        int noiseSizeLandform;
        int landformPad;
        int landformSize;
        int numBlocksInLandformMapTile;

        int scale;
        int riverInnerSize;
        int riverChance;
        int minRiverOceanicity;
        int maxRiverOceanicity;
        float maxHeightForSink;
        float minRiverFlow;
        float maxRiverFlow;
        float flowLoss;
        //int riverRegionEdgeDeviation;
        //int riverRegionCloseToCardinal;
        //float riverWeirdnessMult;
        //float riverCanEnterFlexibility;
        float chanceToFork;

        public RiverMap(long seed, int scale, ICoreServerAPI sapi, List<XZ> requireLandAt) : base(seed) {
            this.scale = scale;
            Sapi = sapi;

            var config = SmoothCoastlinesModSystem.config;
            regionSize = sapi.WorldManager.RegionSize;
            regionChunkSize = sapi.WorldManager.RegionSize / GlobalConstants.ChunkSize;
            oceanScale = config.noiseScale;
            noiseSizeOcean = sapi.WorldManager.RegionSize / TerraGenConfig.oceanMapScale;
            numBlocksInOceanMapTile = TerraGenConfig.oceanMapScale;
            landformPad = TerraGenConfig.landformMapPadding;
            noiseSizeLandform = sapi.WorldManager.RegionSize / TerraGenConfig.landformMapScale;
            landformSize = noiseSizeLandform + 2 * landformPad;
            numBlocksInLandformMapTile = TerraGenConfig.landformMapScale;
            sealevelY = sapi.World.SeaLevel;

            minRiverOceanicity = config.minimumRiverOceanicity;
            maxRiverOceanicity = config.maximumRiverOceanicity;
            riverChance = (int)(config.chanceForRiver * 1000);

            minRiverFlow = config.minimumRiverFlowStrength;
            maxRiverFlow = config.maximumRiverFlowStrength;
            flowLoss = config.flowLossPerRiverSegment;
            maxHeightForSink = config.maxHeightForRiverSink;
            //riverRegionEdgeDeviation = config.riverRegionDirectionRepetitionAllowance;
            //riverRegionCloseToCardinal = config.riverRegionCloseToCardinalWidth;
            //riverWeirdnessMult = config.riverWeirdnessChanceMult;
            //riverCanEnterFlexibility = config.riverRegionCanEnterFlexibility;
            chanceToFork = config.chanceToFork;

            riversByContinent = new Dictionary<XZ, List<RiverData>>();
            heightEstimatesByRegion = new Dictionary<XZ, int[]>(10);
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

            var genRivers = sapi.ModLoader.GetModSystem<GenRivers>();
            coastMap = (CoastMap)genRivers.CoastMap;
        }

        public void SetMapsAndSizesFromRegion(IntDataMap2D coastMap, IntDataMap2D landformHeightMap, int riverInnerSize, int opad, XZ regionCoords) {
            this.regionalCoastMap = coastMap;
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

            var conts = GetContinentsWithRiversEnteringRegion(continentsInfluencingRegion, currentRegion);
            List<RiverData> riverList = null;

            if (conts.Count > 0) {
                var cont = conts.First();
                riverList = riversByContinent[cont];
            }

            var worldX = ConvertLandformMapToWorldCoords(xCoord);
            var worldZ = ConvertLandformMapToWorldCoords(zCoord);
            var halfTile = numBlocksInLandformMapTile / 2;
            for (int x = 0; x < sizeX; x++) {
                for (int z = 0; z < sizeZ; z++) {
                    if (riversByContinent.ContainsKey(currentRegion)) { //This checks for and marks the Regional Center of this Continent
                        result[z * sizeX + x] = 1;
                        continue;
                    }

                    bool noRivers = true;
                    if (riverList != null) {
                        foreach (var river in riverList) {
                            if (river.DoesRiverPassThroughMapTile(currentRegion, new XZ(worldX + (x * numBlocksInLandformMapTile) + halfTile, worldZ + (z * numBlocksInLandformMapTile) + halfTile))) {
                                noRivers = false;
                                result[z * sizeX + x] = 4;
                            }
                        }
                    }

                    if (noRivers) {
                        result[z * sizeX + x] = 0;
                    }
                }
            }

            return result;
        }

        //This will check to see where this Region lies in relation to the nearest continent, using the center-most tile as a polling point.
        //To allow for multiple River sinks starting in both directions, the full four-way check must be done.
        //This handles figuring out which continents are part of this region, then if they need to have rivers initialized at the Region-Level.
        //Afterwards, it will return back to the GenLayer, and then for this specific Region, it's time to generate the map-chunk-level chunks of the rivers that pass through this Region.
        private void PrepareRiversAtContinentalLevel(int xCoord, int zCoord, int sizeX, int sizeZ, ref List<XZ> continentsInfluencingRegion) {
            int oceanX = ConvertRegionToOceanMapCoords(currentRegion.X); //currentRegion.X * noiseSizeOcean - oceanPad;
            int oceanZ = ConvertRegionToOceanMapCoords(currentRegion.Z); //currentRegion.Z * noiseSizeOcean - oceanPad;

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
                List<(RiverPoint, RiverPlottingLogic)> riverStarts = new List<(RiverPoint, RiverPlottingLogic)>(); //Actual World Coordinates - will be the center of the RiverMap Tile XZ.
                //List<float> riverHeightEstimates = new List<float>();

                InitPositionSeed(contCoord.X, contCoord.Z); //Init with the current Continent's X and Z probably? Can reuse this later but on the more defined coordinates for Region and Chunk ect
                RecursivelyIntializeContinents(center, ref visitedRegions, ref riverStarts);

                if (riverStarts.Count <= 0) {
                    continue;
                }

                //Later on, when parts of the River is actually generated, this means we can potentially store data in the save and restore it to expedite the river gen process any time a world or server reboots.
                for (int i = 0; i < riverStarts.Count; i++) { //Might be able to get away with not fully mapping out the chunks for EVERY river all at once... And only go as refined as the chunk level when we are attempting to build that map? Hmm...
                    var sinkPoint = riverStarts[i].Item1;
                    var sinkLogic = riverStarts[i].Item2;
                    //var startRegion = riverStarts[i];

                    //var coastData = coastMap.GenLayer(startRegion.X * riverInnerSize - 1, startRegion.Z * riverInnerSize - 1, riverInnerSize + 1, riverInnerSize + 1);

                    //var startHeight = riverHeightEstimates[i];
                    InitPositionSeed(sinkPoint.worldX, sinkPoint.worldZ); //Lets just init this based on the sink's World Coords and go from there.
                    //var regionX = ConvertLandformMapToRegionCoords(ConvertWorldCoordsToLandformMap(sinkPoint.worldX));
                    //var regionZ = ConvertLandformMapToRegionCoords(ConvertWorldCoordsToLandformMap(sinkPoint.worldZ));
                    var oceanicityForPoint = oceanMap.GetOceanicityAt(ConvertWorldCoordsToOceanMap(sinkPoint.worldX), ConvertWorldCoordsToOceanMap(sinkPoint.worldZ));
                    var percentStrength = NextInt(100) / 100f;
                    var waterFlow = GameMath.Lerp(minRiverFlow, maxRiverFlow, percentStrength);
                    RiverData activeRiverData = new RiverData(contCoord, waterFlow);

                    //Set up and push the first point.
                    Stack<(RiverPoint, RiverPlottingLogic)> activePoints = new Stack<(RiverPoint, RiverPlottingLogic)>();
                    //RiverRegion sinkRegion = new RiverRegion(workingContinentalRivers, new XZ(regionX, regionZ));
                    //RiverSegment sinkSegment = new RiverSegment(sinkRegion);
                    //RiverPoint sinkPoint = new RiverPoint(sinkPoint.X, sinkPoint.Y, sinkPoint.Z, oceanicityForPoint, waterFlow, sinkSegment);
                    //sinkPoint.UpdateParentSegment(sinkSegment);
                    sinkPoint.UpdateFlow(waterFlow);
                    sinkPoint.UpdateOceanicity(oceanicityForPoint);
                    //sinkSegment.AddPointToSegment(sinkPoint);
                    //sinkRegion.AddPrimarySegment(sinkSegment);
                    //workingContinentalRivers.AddRegionToRiver(sinkRegion);
                    //PrimaryRiverLogic sinkLogic = new PrimaryRiverLogic(baseChanceAmount);
                    activePoints.Push((sinkPoint, sinkLogic));
                    RiverPoint curPoint = null;
                    RiverPlottingLogic curLogic = null;
                    RiverSegment curSegment = null;
                    RiverRegion curRegion = null;

                    //XZ centerOffset = GetRegionDirectionTo(center.regionCoords, sinkRegion.regionCoords); //This is the number of region steps in each direction to get to the center. Rivers should ideally try to head in this direction? Maybe not, honestly.

                    //Each River needs a starting strength value, which will decrement for each region added on. Configurable range for maximum and minimum loss?
                    //For each added Region on the river, need to compute the strength loss from Downstream to Upstream, any possible forks or other special things that change the direction, and the exact world coordinates along the edge it needs to line up at. Y doesn't matter until the Chunk level, since the blocks won't actually exist until then.
                    while (activePoints.Count > 0) {
                        var curTuple = activePoints.Pop();
                        curPoint = curTuple.Item1;
                        curLogic = curTuple.Item2;
                        /*waterFlow -= unscaledFlowLoss;
                        if (waterFlow < 0) {
                            continue;
                        }*/
                        if (curPoint.flowStrength <= 0) {
                            continue;
                        }

                        //curRegion will need to see if this point's region exists already in this RiverData, and just grab that one, or if it needs to create a new RiverRegion.
                        TryGetOrCreateRiverRegion(ref activeRiverData, ref curRegion, ref curPoint);

                        //curSegment will always need to be created here, and add the first point.
                        if (curPoint.HasDownstream()) { //This catches all points except for the Sink!
                            if (curPoint.pointAfterConfluence) { //If it is the start of a fork, this will be true.
                                curSegment = new RiverSegment(curRegion);
                                curPoint.GetDownstream().parentSegment.AddForkSegment(curSegment, curPoint.GetDownstream());
                            } else { //Otherwise, it is a normal point, just link up the segments and we are good!
                                curSegment = new RiverSegment(curRegion, curPoint.GetDownstream().parentSegment); //Links the segments in the constructor
                            }
                        } else { //The point is a Sink! Just create the segment.
                            curSegment = new RiverSegment(curRegion);
                        }
                        curSegment.AddPointToSegment(curPoint);

                        //Potentionally refine things later on:
                        // Does not account for collisions with other Rivers, but, MIGHT lead to avoiding them naturally? If spread out enough at least

                        //Wanting to rework how these are plotted.
                        // Instead of point by point, lets work on a full segment of points at a time.
                        // Should be helpful in making this cleaner. Go until the segment ends through hitting the max points per segment, or it cannot find any valid points.
                        // Then add all points to the segment, and add the segment to the Region.

                        RiverPoint primaryPoint;
                        bool iterateSegment = true;
                        List<RiverPoint> generatedForkedPoints = new List<RiverPoint>();
                        while (iterateSegment) {
                            //Important to remember for all direction-related XZ pairs: -Z = N, +Z = S, -X = W, +X = E
                            //This direction is the direction it last came from - we do not want to backtrack, so this direction is always off limits.
                            if (curPoint.HasDownstream()) { //If it has no Downstream, the sinkLogic provided by the Sink Choice method already has accounted for 'Sea' tiles on the Coastmap.
                                var downDir = curPoint.GetDirectionTo(curPoint.GetDownstream());
                                curLogic.PreventDoubleBack(downDir);
                            }

                            //Instantiate the next step data, then account for any possible prior-direction to avoid doubling back
                            //Don't allow the river to re-enter the same tile after it's passed through it once.
                            PreventReenterTiles(curPoint, ref curLogic);

                            //Calculate and figure out the Oceanicities for any still-valid direction!
                            ProcessOceanicityPossibilities(curPoint, ref curLogic);

                            //Then figure out the estimated y-heights in the center of each tile.
                            ProcessHeightMapPossibilities(curPoint, ref curLogic);
                            
                            var fullChance = curLogic.GetFullChance();
                            if (fullChance <= 0) {
                                //If the chance is zero (or less), the Primary River has concluded here. There's no more valid places for it to go without it being weird - Time to shift it into a Tributary logic.
                                //Perhaps this might be a way to add in some special River effects to let it continue on regardless?
                                curLogic = curLogic.ShiftLogic(baseChanceAmount);

                                if (curLogic == null) {
                                    iterateSegment = false; //Stops the loop if this is the case!
                                }
                                continue; //This will only break out of this loop when the river is at a complete end at this point. Otherwise it just swaps the logic and continues.
                            }

                            var rngPrimaryChance = NextInt(fullChance);
                            primaryPoint = curLogic.ChooseNextPointDirection(curPoint, numBlocksInLandformMapTile, rngPrimaryChance);
                            curPoint.AddUpstreamPoint(primaryPoint);

                            //Where would be best to handle a possible Fork? Should be queued BEFORE the main route is queued to ensure it is added in the right order.
                            fullChance = curLogic.GetFullChance();
                            if (fullChance > 0) {
                                var doFork = NextInt(100);
                                if (doFork <= (chanceToFork * 100)) {
                                    var rngForkChance = NextInt(fullChance);
                                    RiverPoint forkPoint = curLogic.ChooseNextPointDirection(curPoint, numBlocksInLandformMapTile, rngForkChance);
                                    curPoint.AddConfluenceUpstream(forkPoint); //Always the start of a new Segment! This marks it as the point after a confluence, the fork's first point.
                                    generatedForkedPoints.Add(forkPoint);

                                    TributaryRiverLogic forkLogic = new TributaryRiverLogic(baseChanceAmount);
                                    activePoints.Push((forkPoint, forkLogic));
                                }
                            }

                            //Done with this current point, time to shift to the next one and refresh the logic.
                            curLogic = curLogic.ChainLogic(baseChanceAmount);
                            curPoint = primaryPoint;

                            if (curSegment.NumPointsInSegment() < RiverSegment.maxPointsPerSegment) {
                                var primaryX = ConvertLandformMapToRegionCoords(ConvertWorldCoordsToLandformMap(curPoint.worldX));
                                var primaryZ = ConvertLandformMapToRegionCoords(ConvertWorldCoordsToLandformMap(curPoint.worldZ));
                                if (curSegment.PointValidForSegmentsRegion(new XZ(primaryX, primaryZ))) { //If the point is valid for this segment's region, it can be added to it.
                                    curSegment.AddPointToSegment(curPoint);
                                } else {
                                    iterateSegment = false;
                                }
                            } else {
                                iterateSegment = false;
                            }
                        }

                        //After this point, the segment is completed. All points should be added to it that should be, and all linked up.
                        //Run the Segment's point averaging and post-processing passes here. Then also account for curPoint, and all possible forked points.
                        //First just equalize the flow value through the whole Segment, and then copy the downstream flow to the next curPoint and all possible forkedPoints
                        curSegment.AverageOutFlows(flowLoss);
                        curPoint.UpdateFlow(curPoint.GetDownstream().flowStrength);
                        for (int f = 0; f < generatedForkedPoints.Count; f++) {
                            generatedForkedPoints[f].UpdateFlow(generatedForkedPoints[f].GetDownstream().flowStrength);
                        }

                        //Then ensure each point has a valid Y-Coordinate set. This is likely also where we will want to detect for Waterfalls and other special situations
                        int startY;
                        if (curSegment.downstream != null) {
                            startY = curSegment.downPoint.GetDownstream().worldY;
                        } else {
                            startY = sealevelY;
                        }

                        var pointCount = curSegment.NumPointsInSegment();
                        for (int p = 0; p < pointCount; p++) {

                        }

                        //Finally, add this segment to the region.
                        if (!curSegment.forkedSegment) {
                            curRegion.AddPrimarySegment(curSegment);
                        } else {
                            curRegion.AddForkSegment(curSegment);
                        }

                        //Now queue up the Primary River continuation here (if the river continues, logic will be null if it does not.) - Forks have been queued already in the loop. When it comes their time to run, they will be processed the same as the Primary, only after it has concluded entirely.
                        if (curLogic != null) {
                            activePoints.Push((curPoint, curLogic));
                        }

                        /*RiverRegion forkRegion = GetOrCreateRiverRegion(ref forkPoint, ref curPoint, ref activeRiverData);
                        RiverSegment forkSegment = new RiverSegment(forkRegion, curPoint.parentSegment);
                        forkPoint.UpdateParentSegment(forkSegment);
                        forkSegment.AddPointToSegment(forkPoint);

                        var forkSuccess = curPoint.parentSegment.AddForkSegment(forkSegment, curPoint);
                        if (forkSuccess) {
                            forkRegion.AddForkSegment(forkSegment);

                            TributaryRiverLogic forkLogic = new TributaryRiverLogic(baseChanceAmount);
                            activePoints.Enqueue((forkPoint, forkLogic));
                        } else {
                            if (!(forkRegion.primarySegments.Count > 0 || forkRegion.forkedSegments.Count > 0)) {
                                activeRiverData.RemoveRegionFromRiver(forkRegion);
                            }
                            curPoint.parentSegment.RemoveForkSegment(curPoint);
                            curPoint.CancelConfluence(forkPoint);
                        }

                        RiverRegion primaryRegion = GetOrCreateRiverRegion(ref primaryPoint, ref curPoint, ref activeRiverData);

                        RiverSegment primarySegment;
                        bool primarySuccess = curPoint.parentSegment.PointValidForSegmentsRegion(primaryRegion.regionCoords);
                        if (primarySuccess) {
                            primarySuccess = curPoint.parentSegment.AddPointToSegment(primaryPoint);
                        }

                        if (primarySuccess) {
                            primarySegment = curPoint.parentSegment;
                        } else {
                            primarySegment = new RiverSegment(primaryRegion, curPoint.parentSegment);
                            primarySegment.AddPointToSegment(primaryPoint);
                            primaryRegion.AddPrimarySegment(primarySegment);
                        }
                        primaryPoint.UpdateParentSegment(primarySegment);

                        RiverPlottingLogic newLogic = curLogic.ChainLogic(baseChanceAmount);
                        activePoints.Enqueue((primaryPoint, newLogic));*/
                    }

                    continentalRivers.Add(activeRiverData);
                }
            }
        }

        private void RecursivelyIntializeContinents(RiverRegionStep curRegion, ref BitArray visitedRegions, ref List<(RiverPoint, RiverPlottingLogic)> riverStarts) { //Lets try to just poll the center of the region for it's Oceanicity and see if that's sufficient
            visitedRegions[curRegion.visitedCoords.Z * maxRegionSteps + curRegion.visitedCoords.X] = true;
            int centerOceanRegionX = ConvertRegionToOceanMapCoords(curRegion.regionCoords.X, oceanSize / 2); //(curRegion.regionCoords.X * noiseSizeOcean - oceanPad) + (oceanSize / 2); //Will this need to be a few checks? Like 4 points or something maybe for a finer detail.
            int centerOceanRegionZ = ConvertRegionToOceanMapCoords(curRegion.regionCoords.Z, oceanSize / 2); //(curRegion.regionCoords.Z * noiseSizeOcean - oceanPad) + (oceanSize / 2);

            var oceanicity = oceanMap.GetOceanicityAt(centerOceanRegionX, centerOceanRegionZ);

            if (oceanicity >= minRiverOceanicity && oceanicity <= maxRiverOceanicity) {
                if (NextInt(1000) < riverChance) {
                    ProcessAndPickSinkTile(curRegion, ref riverStarts);
                    return;
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

        private void ProcessAndPickSinkTile(RiverRegionStep curRegion, ref List<(RiverPoint, RiverPlottingLogic)> riverStarts) {
            var landformX = ConvertRegionToLandformMapCoords(curRegion.regionCoords.X);
            var landformZ = ConvertRegionToLandformMapCoords(curRegion.regionCoords.Z);
            float[] heights = new float[noiseSizeLandform * noiseSizeLandform];

            for (int x = 0; x < noiseSizeLandform; x++) {
                for (int z = 0; z < noiseSizeLandform; z++) {
                    var height = this.landformMap.GetHeightMapAt(landformX + x, landformZ + z);
                    if (height > maxHeightForSink) {
                        height = -1;
                    }
                    heights[z * noiseSizeLandform + x] = height;
                }
            }

            bool[] coasts = new bool[noiseSizeLandform * noiseSizeLandform]; //Since the Coastmap and Rivermap are on the same scale as the Landforms, these are interchangable cause they are the same. landformSize factors in the padding as well, so it is NOT.

            IntDataMap2D oceanMap = new IntDataMap2D {
                Data = this.oceanMap.GenLayer(curRegion.regionCoords.X * noiseSizeOcean - oceanPad, curRegion.regionCoords.Z * noiseSizeOcean - oceanPad, oceanSize, oceanSize),
                Size = oceanSize,
                TopLeftPadding = oceanPad,
                BottomRightPadding = oceanPad
            };
            IntDataMap2D landformMap = new IntDataMap2D {
                Data = this.landformMap.GenLayer(curRegion.regionCoords.X * noiseSizeLandform - landformPad, curRegion.regionCoords.Z * noiseSizeLandform - landformPad, landformSize, landformSize),
                Size = landformSize,
                TopLeftPadding = landformPad,
                BottomRightPadding = landformPad
            };
            var genTerraPrety = Sapi.ModLoader.GetModSystem<GenTerraPrety>();
            var landLerpMap = genTerraPrety.GetOrCreateLerpedLandformMap(landformMap, curRegion.regionCoords.X, curRegion.regionCoords.Z);
            coastMap.SetCoastAndLandformMaps(oceanMap, landLerpMap, landformMap.InnerSize, landformMap.InnerSize);
            var coastMapForRegion = coastMap.GenLayerAndAddToCache(curRegion.regionCoords.X * noiseSizeLandform, curRegion.regionCoords.Z * noiseSizeLandform, noiseSizeLandform + 1, noiseSizeLandform + 1);
            
            int possibleCoastTiles = 0;
            for (int x = 0; x < noiseSizeLandform; x++) {
                for (int z = 0; z < noiseSizeLandform; z++) {
                    if ((x > 0 && x < noiseSizeLandform - 1) && (z > 0 && z < noiseSizeLandform - 1) && heights[z * noiseSizeLandform + x] > -1 && coastMapForRegion[z * noiseSizeLandform + x] == (int)EnumCoasts.Coast) {
                        coasts[z * noiseSizeLandform + x] = true; //Lets only ever use points that are not on the Regional Border
                        possibleCoastTiles++;
                    } else {
                        coasts[z * noiseSizeLandform + x] = false; //But it still initializes the outer border as false to avoid null problems just in case.
                    }
                }
            }

            if (possibleCoastTiles != 0) {
                var sinkChoice = NextInt(possibleCoastTiles);
                for (int x = 1; x < noiseSizeLandform - 1; x++) {
                    for (int z = 1; z < noiseSizeLandform - 1; z++) {
                        if (!coasts[z * noiseSizeLandform + x]) {
                            continue;
                        }

                        sinkChoice--;
                        if (sinkChoice <= 0) {
                            var pointX = ConvertLandformMapToWorldCoords(landformX + x, (numBlocksInLandformMapTile / 2));
                            var pointZ = ConvertLandformMapToWorldCoords(landformZ + z, (numBlocksInLandformMapTile / 2));
                            RiverPoint sinkPoint = new RiverPoint(pointX, sealevelY, pointZ);

                            RiverPlottingLogic sinkLogic = new PrimaryRiverLogic(baseChanceAmount);
                            for (int i = -1; i < 2; i++) { //X offset
                                for (int j = -1; j < 2; j++) { //Z offset - This pass will ensure that any Ocean tiles are marked as 'do not traverse' in the logic.
                                    if (i == 0 && j == 0) {
                                        continue;
                                    }

                                    if (coastMapForRegion[(z + j) * noiseSizeLandform + (x + i)] == (int)EnumCoasts.Sea) {
                                        sinkLogic.PreventDirectionChoice(new XZ(i, j));
                                    }
                                }
                            }

                            if (sinkLogic.GetFullChance() > 0) {
                                riverStarts.Add((sinkPoint, sinkLogic));
                                break;
                            } else {
                                sinkChoice++;
                            }
                        }
                    }

                    if (sinkChoice <= 0) {
                        break;
                    }
                }
            }
        }

        private void PreventReenterTiles(RiverPoint curPoint, ref RiverPlottingLogic logic) {
            for (int x = -1; x < 2; x++) {
                for (int z = -1; z < 2; z++) {
                    if (x == 0 && z == 0) {
                        continue;
                    }

                    var potentialWorldX = curPoint.worldX + (numBlocksInLandformMapTile * x);
                    var potentialWorldZ = curPoint.worldZ + (numBlocksInLandformMapTile * z);

                    var potentialRegionX = ConvertLandformMapToRegionCoords(ConvertWorldCoordsToLandformMap(potentialWorldX));
                    var potentialRegionZ = ConvertLandformMapToRegionCoords(ConvertWorldCoordsToLandformMap(potentialWorldZ));

                    if (curPoint.parentSegment.riverRegion.river.DoesRiverPassThroughMapTile(new XZ(potentialRegionX, potentialRegionZ), new XZ(potentialWorldX, potentialWorldZ))) {
                        logic.PreventDirectionChoice(new XZ(x, z));
                    }
                }
            }
        }

        private void ProcessOceanicityPossibilities(RiverPoint curPoint, ref RiverPlottingLogic logic) {
            for (int x = -1; x < 2; x++) {
                for (int z = -1; z < 2; z++) {
                    if (x == 0 && z == 0) {
                        continue;
                    }

                    var potentialX = curPoint.worldX + (numBlocksInLandformMapTile * x);
                    var potentialZ = curPoint.worldZ + (numBlocksInLandformMapTile * z);

                    var oceanX = ConvertWorldCoordsToOceanMap(potentialX);
                    var oceanZ = ConvertWorldCoordsToOceanMap(potentialZ);

                    var oceanicity = oceanMap.GetOceanicityAt(oceanX, oceanZ);
                    logic.SetOceanicityOfDir(new XZ(x, z), oceanicity, curPoint.oceanicity);
                }
            }
        }

        private void ProcessHeightMapPossibilities(RiverPoint curPoint, ref RiverPlottingLogic logic) {
            for (int x = -1; x < 2; x++) {
                for (int z = -1; z < 2; z++) {
                    if (x == 0 && z == 0) {
                        continue;
                    }

                    var potentialX = curPoint.worldX + (numBlocksInLandformMapTile * x);
                    var potentialZ = curPoint.worldZ + (numBlocksInLandformMapTile * z);

                    var landformX = ConvertWorldCoordsToLandformMap(potentialX);
                    var landformZ = ConvertWorldCoordsToLandformMap(potentialZ);

                    var heightmap = landformMap.GetHeightMapAt(landformX, landformZ);
                    logic.SetHeightOfDir(new XZ(x, z), heightmap, curPoint.landformHMHeight);
                }
            }
        }

        //This may be insanely taxing on processing power - but a way to optimize it might be generate the whole region's estimated y-block height, then cache it.
        //If cached by Region Coordinate pair, then it can be accessed similarly to the LandformLerpMap! This will help a lot actually.
        private void ProcessHeightPossibilities(RiverPoint curPoint, ref RiverPlottingLogic rngChances) {
            var regionCoords = curPoint.parentSegment.riverRegion.regionCoords;
            var regionalX = ConvertRegionToLandformMapCoords(regionCoords.X);
            var regionalZ = ConvertRegionToLandformMapCoords(regionCoords.Z);
            int[] heightEstimates = GetOrLoadHeightEstimatesForRegion(regionCoords);
            IntDataMap2D heightEstimatesMap = new IntDataMap2D {
                Data = heightEstimates,
                Size = noiseSizeLandform + 2 * 2,
                TopLeftPadding = 2,
                BottomRightPadding = 2
            };

            for (int x = -1; x < 2; x++) {
                for (int z = -1; z < 2; z++) {
                    if (x == 0 && z == 0) {
                        continue;
                    }

                    var potentialX = curPoint.worldX + (numBlocksInLandformMapTile * x);
                    var potentialZ = curPoint.worldZ + (numBlocksInLandformMapTile * z);

                    var landformX = ConvertWorldCoordsToLandformMap(potentialX);
                    var landformZ = ConvertWorldCoordsToLandformMap(potentialZ);

                    //SmoothCoastlinesModSystem.Logger.Warning("X: " + (landformX - regionalX) + " Z: " + (landformZ - regionalZ));
                    var height = heightEstimatesMap.GetUnpaddedInt((landformX - regionalX), (landformZ - regionalZ)); //heightEstimates[(landformZ - regionalZ) * noiseSizeLandform + (landformX - regionalX)];
                    rngChances.SetHeightOfDir(new XZ(x, z), height, curPoint.worldY);
                }
            }
        }

        private int[] GetOrLoadHeightEstimatesForRegion(XZ regionCoords) {
            if (heightEstimatesByRegion.TryGetValue(regionCoords, out var height)) {
                return height;
            }

            IntDataMap2D oceanMap = new IntDataMap2D {
                Data = this.oceanMap.GenLayer(regionCoords.X * noiseSizeOcean - oceanPad, regionCoords.Z * noiseSizeOcean - oceanPad, oceanSize, oceanSize),
                Size = oceanSize,
                TopLeftPadding = oceanPad,
                BottomRightPadding = oceanPad
            };
            IntDataMap2D landformMap = new IntDataMap2D {
                Data = this.landformMap.GenLayer(regionCoords.X * noiseSizeLandform - landformPad, regionCoords.Z * noiseSizeLandform - landformPad, landformSize, landformSize),
                Size = landformSize,
                TopLeftPadding = landformPad,
                BottomRightPadding = landformPad
            };
            var genTerraPrety = Sapi.ModLoader.GetModSystem<GenTerraPrety>();
            var landLerpMap = genTerraPrety.GetOrCreateLerpedLandformMap(landformMap, regionCoords.X, regionCoords.Z);
            coastMap.SetCoastAndLandformMaps(oceanMap, landLerpMap, landformMap.InnerSize, landformMap.InnerSize);
            var heights = coastMap.GetEstimatedYHeights(regionCoords.X * noiseSizeLandform - 2, regionCoords.Z * noiseSizeLandform - 2, noiseSizeLandform + 2 * 2, noiseSizeLandform + 2 * 2);
            heightEstimatesByRegion.Add(regionCoords, heights);

            return heights;
        }

        /*private RiverRegion GetOrCreateRiverRegion(ref RiverPoint newPoint, ref RiverPoint curPoint, ref RiverData workingContRivers) {
            RiverRegion rivRegion;
            var pointRegionX = ConvertLandformMapToRegionCoords(ConvertWorldCoordsToLandformMap(newPoint.worldX));
            var pointRegionZ = ConvertLandformMapToRegionCoords(ConvertWorldCoordsToLandformMap(newPoint.worldZ));
            XZ pointRegionXZ = new XZ(pointRegionX, pointRegionZ);

            if (curPoint.parentSegment.PointValidForSegmentsRegion(pointRegionXZ)) { //If this point is still within curPoint's region, then we can just add a new forked segment to it.
                rivRegion = curPoint.parentSegment.riverRegion;
            } else { //If it is not, we will have to make a new RiverRegion as well and add it to the RiverData, along with forking the segment proper!
                if (workingContRivers.DoesRiverPassThroughRegion(pointRegionXZ)) {
                    rivRegion = workingContRivers.GetRegionAt(pointRegionXZ);
                } else {
                    rivRegion = new RiverRegion(workingContRivers, pointRegionXZ);
                    curPoint.parentSegment.riverRegion.ConnectRegion(rivRegion);
                    workingContRivers.AddRegionToRiver(rivRegion);
                }
            }

            return rivRegion;
        }*/

        private void TryGetOrCreateRiverRegion(ref RiverData workingRiver, ref RiverRegion region, ref RiverPoint curPoint) {
            var pointRegionX = ConvertLandformMapToRegionCoords(ConvertWorldCoordsToLandformMap(curPoint.worldX));
            var pointRegionZ = ConvertLandformMapToRegionCoords(ConvertWorldCoordsToLandformMap(curPoint.worldZ));
            XZ pointRegionXZ = new XZ(pointRegionX, pointRegionZ);

            if (region != null) { //If it is not null, check if this region is already valid for this point. If so, no need to do anything!
                if (region.regionCoords.X == pointRegionX && region.regionCoords.Z == pointRegionZ) {
                    return;
                }
            }

            //Otherwise, either the region has not been found yet, or it isn't valid anymore, so we need to update it.
            if (workingRiver.DoesRiverPassThroughRegion(pointRegionXZ)) {
                region = workingRiver.GetRegionAt(pointRegionXZ);
            } else {
                region = new RiverRegion(workingRiver, pointRegionXZ);
                workingRiver.AddRegionToRiver(region);
            }
        }

        private float GetAverageHeightForRegion(XZ regionCoords) {
            int landformSizeFourth = landformSize / 4;
            int upLeftHeightX = ConvertRegionToLandformMapCoords(regionCoords.X, landformSizeFourth); //(regionCoords.X * noiseSizeLandform - landformPad) + landformSizeFourth; //The upper left quadrant's centermost tile, this plus the other 3 divided by 4 can be the quick heightmap estimate?
            int upLeftHeightZ = ConvertRegionToLandformMapCoords(regionCoords.Z, landformSizeFourth); //(regionCoords.Z * noiseSizeLandform - landformPad) + landformSizeFourth;

            var avgHeight = landformMap.GetHeightMapAt(upLeftHeightX, upLeftHeightZ);
            avgHeight += landformMap.GetHeightMapAt(upLeftHeightX + (2 * landformSizeFourth), upLeftHeightZ);
            avgHeight += landformMap.GetHeightMapAt(upLeftHeightX, upLeftHeightZ + (2 * landformSizeFourth));
            avgHeight += landformMap.GetHeightMapAt(upLeftHeightX + (2 * landformSizeFourth), upLeftHeightZ + (2 * landformSizeFourth));
            avgHeight /= 4;

            return avgHeight;
        }

        //For reference: North -Z, South +Z, West -X, and East +X
        private float GetAverageHeightMapValuesOfCardinal(XZ regionCoords, XZ cardinalDir) {
            var landformOriginRegionX = ConvertRegionToLandformMapCoords(regionCoords.X); //regionCoords.X * noiseSizeLandform - landformPad;
            var landformOriginRegionZ = ConvertRegionToLandformMapCoords(regionCoords.Z); //regionCoords.Z * noiseSizeLandform - landformPad;
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
            //var regionAvgHeight = downRegion.averageHeight;
            var landformOriginRegionX = ConvertRegionToLandformMapCoords(downRegion.regionCoords.X); //downRegion.regionCoords.X * noiseSizeLandform - landformPad;
            var landformOriginRegionZ = ConvertRegionToLandformMapCoords(downRegion.regionCoords.Z); //downRegion.regionCoords.Z * noiseSizeLandform - landformPad;
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
                /*if (height >= downRegion.averageHeight) {
                    int weight = (int)(height * maxTileWeight);
                    tileWeights[new XZ(landformOriginRegionX, landformOriginRegionZ)] = weight;
                    totalWeight += weight;
                }*/
            }

            int rngFactor = NextInt(totalWeight);
            foreach (var tile in tileWeights) {
                rngFactor -= tile.Value;
                if (rngFactor < 0) {
                    XZ chosenTile = tile.Key;

                    float tileRegionX = ConvertLandformMapToRegionCoords(chosenTile.X); //(chosenTile.X + landformPad) / (float)noiseSizeLandform;
                    float tileRegionZ = ConvertLandformMapToRegionCoords(chosenTile.Z); //(chosenTile.Z + landformPad) / (float)noiseSizeLandform;

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
                var list = riversByContinent.TryGetValue(cont);
                if (list != null && !contList.Contains(cont)) {
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

        /*private XZ ConvertToOceanMapCoords(XZ regionCoords, int offset = 0) {
            return new XZ((regionCoords.X * noiseSizeOcean - oceanPad) + offset, (regionCoords.Z * noiseSizeOcean - oceanPad) + offset);
        }*/

        // --- These are going to lose precision when moving upwards to a higher scale! So do not use it to convert from ocean to landform scales! ---

        private int ConvertRegionToOceanMapCoords(int coord, int offset = 0) {
            return (coord * noiseSizeOcean - oceanPad) + offset;
        }

        private int ConvertOceanMapToRegionCoords(int oceanCoord) {
            return (oceanCoord + oceanPad) / noiseSizeOcean;
        }

        private int ConvertOceanMapToWorldCoords(int coord, int offset = 0) {
            return (coord * numBlocksInOceanMapTile) + offset;
        }

        private int ConvertWorldCoordsToOceanMap(int worldCoord) {
            return (worldCoord / numBlocksInOceanMapTile);
        }

        private int ConvertRegionToLandformMapCoords(int coord, int offset = 0) {
            return (coord * noiseSizeLandform - landformPad) + offset;
        }

        private int ConvertLandformMapToRegionCoords(int landformCoord) {
            return (landformCoord + landformPad) / noiseSizeLandform;
        }

        private int ConvertLandformMapToWorldCoords(int coord, int offset = 0) {
            return (coord * numBlocksInLandformMapTile) + offset;
        }

        private int ConvertWorldCoordsToLandformMap(int worldCoord) {
            return (worldCoord / numBlocksInLandformMapTile);
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
