using SmoothCoastlines.LandformHeights;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.Rivers {

    public class CoastMap : MapLayerBase {

        internal ICoreServerAPI sapi;
        internal IntDataMap2D OceanMap;
        internal LerpedWeightedIndex2DMap LandformLerpMap;
        internal Dictionary<XZ, int[]> coastCache = new Dictionary<XZ, int[]>();

        int scale;
        int terrainGenOctaves;
        int landformLength;
        int coastalInnerSize;
        int landformInnerSize;
        int hardMinimumOceanicity;
        int softMinimumOceanicity;
        int maximumOceanicity;
        float[][] terrainYThresholds;
        NewNormalizedSimplexFractalNoise terrainNoise;

        const double terrainDistortionMultiplier = 4.0; //Copied from GenTerra
        const double terrainDistortionThreshold = 40.0;
        const double maxDistortionAmount = (55 + 40 + 30 + 10) * SimplexNoiseOctave.MAX_VALUE_2D_WARP;
        const short aboveSeaY = 0;
        const short alreadySet = -1;
        const int landEnumVal = (int)EnumCoasts.Land;
        const int seaEnumVal = (int)EnumCoasts.Sea;
        const int coastEnumVal = (int)EnumCoasts.Coast;
        float oceanicityFac;

        SimplexNoise distort2dx;
        SimplexNoise distort2dz;
        float noiseScale;

        public CoastMap(long seed, int scale, ICoreServerAPI sapi) : base(seed) { //The seed might not be needed here, since this just serves as a launching point for building the "coastal map"
            this.scale = scale;
            this.sapi = sapi;

            this.hardMinimumOceanicity = SmoothCoastlinesModSystem.config.hardMinimumCoastalOceanicity;
            this.softMinimumOceanicity = SmoothCoastlinesModSystem.config.softMinimumCoastalOceanicity;
            this.maximumOceanicity = SmoothCoastlinesModSystem.config.maximumCoastalOceanicity;
            noiseScale = Math.Max(1, (sapi.WorldManager.MapSizeY - 64) / 256f);
            distort2dx = new SimplexNoise(
                new double[] { 55, 40, 30, 10 },
                scaleAdjustedFreqs(new double[] { 1 / 5.0, 1 / 2.50, 1 / 1.250, 1 / 0.65 }, noiseScale),
                sapi.World.Seed + 9876 + 0
            );
            distort2dz = new SimplexNoise(
                new double[] { 55, 40, 30, 10 },
                scaleAdjustedFreqs(new double[] { 1 / 5.0, 1 / 2.50, 1 / 1.250, 1 / 0.65 }, noiseScale),
                sapi.World.Seed + 9876 + 2
            );
            oceanicityFac = (sapi.WorldManager.MapSizeY - 64) / 256 * 0.33333f;

            coastCache.Clear();
        }

        public void SetThresholdAndNoise(float[][] thresholds, NewNormalizedSimplexFractalNoise terrainNoiseDuplicate) {
            terrainYThresholds = thresholds;
            terrainNoise = terrainNoiseDuplicate;
        }

        public void SetCoastAndLandformMaps(IntDataMap2D oceanMap, LerpedWeightedIndex2DMap landformLerpMap, int landformInnerSize, int coastalInnerSize) {
            this.OceanMap = oceanMap;
            this.LandformLerpMap = landformLerpMap;
            this.landformInnerSize = landformInnerSize;
            this.coastalInnerSize = coastalInnerSize;
            terrainGenOctaves = TerraGenConfig.GetTerrainOctaveCount(sapi.WorldManager.MapSizeY - 64);
            landformLength = LandformHeightNoise.landforms.LandFormsByIndex.Length;
        }

        public override int[] GenLayer(int xCoord, int zCoord, int sizeX, int sizeZ) { //xCoord and zCoord are the coordinates of the RegionX/Z * Width of the Map in world Coordinates, so the very first coordinate in the upper left of the map, at the CoastMap Scale (equal to Landform Map)
            XZ regionCoord = new XZ(xCoord, zCoord);
            if (coastCache.TryGetValue(regionCoord, out int[] cachedCoast)) {
                SmoothCoastlinesModSystem.Logger.Warning("Found region X: " + xCoord + " Z: " + zCoord + " in the cache already! Returning it instead.");
                return cachedCoast;
            }
            
            BitArray visitedPoints = new BitArray(sizeX * sizeZ);
            List<VectorXZInt> pointsToStartFlood = new List<VectorXZInt>();
            List<VectorXZInt> pointsToExamine = new List<VectorXZInt>();
            short[] terrainResults = new short[sizeX * sizeZ]; //This will hold the calculations of the TerrainNoise so that it ideally only has to be done once.
            short[] terrainOceanicityResults = new short[sizeX * sizeZ]; //This is the result of if the TerrainNoise also factored the Oceanicity into it.
            float[] oceanicityResults = new float[sizeX * sizeZ]; //This holds the actual oceanicity values of each map area
            int[] result = new int[sizeX * sizeZ];
            for (int i = 0; i < result.Length; i++) { //Init all arrays to the default state
                terrainResults[i] = aboveSeaY;
                terrainOceanicityResults[i] = aboveSeaY;
                result[i] = landEnumVal; //(int)EnumCoasts.Land; The Enum is set up so that 0 is Land, 1 is Sea, and 2 is a Ocean Coastline. Quicker to simply just use a constant here instead of converting the Enum? Dunno how it compiles exactly.
            }
            visitedPoints.SetAll(false);
            pointsToStartFlood.Clear();
            pointsToExamine.Clear();

            double[] lerpedAmps = new double[terrainGenOctaves];
            double[] lerpedTh = new double[terrainGenOctaves];
            float[] landformWeights = new float[landformLength];
            //int regionX = xCoord / coastalInnerSize;
            //int regionZ = zCoord / coastalInnerSize;

            const int chunksize = GlobalConstants.ChunkSize; //Number of Blocks in a Chunk.
            const float chunkBlockDelta = 1.0f / chunksize;

            var genTerraPrety = sapi.ModLoader.GetModSystem<GenTerraPrety>();
            int regionChunkSize = sapi.WorldManager.RegionSize / chunksize; //Number of Chunks in the Region.
            float chunkPixelSize = (landformInnerSize / regionChunkSize); //InnerSize is the map's width/length of 1 region

            int landformHalfScale = TerraGenConfig.landformMapScale / 2;
            double verticalNoiseRelativeFrequency = 0.5 / TerraGenConfig.terrainNoiseVerticalScale;

            for (int x = 0; x < sizeX; x++) { //First Pass: Gather as much of the data points as possible, ideally all of them here. Should only need the Oceanicity and TerrainNoise Results?
                for (int z = 0; z < sizeZ; z++) { //Also set up the initial points if they are valid.
                    float baseX = (x / 2) * chunkPixelSize;
                    float baseZ = (z / 2) * chunkPixelSize;
                    float halfPixelSize = chunkPixelSize / 2;
                    int worldX = (xCoord * TerraGenConfig.landformMapScale) + (x * TerraGenConfig.landformMapScale) + landformHalfScale; //The World Coords at the center of this map pixel.
                    int worldZ = (zCoord * TerraGenConfig.landformMapScale) + (z * TerraGenConfig.landformMapScale) + landformHalfScale;

                    int oceanicity = OceanMap.GetUnpaddedInt((x * OceanMap.InnerSize) / coastalInnerSize, (z * OceanMap.InnerSize) / coastalInnerSize); //X and Z here are the coordinates of the CoastMap's entries for this Region it is building.
                    oceanicityResults[z * sizeX + x] = oceanicity; //The above translates it into proper coordinates for the OceanMap's scale, which can (and does) differ from the CoastMap's scale.

                    if (oceanicity < hardMinimumOceanicity) { //This is comparing the raw values from the map, so it will be from 0 - 255 for Oceanicity.
                        terrainResults[z * sizeX + x] = alreadySet;
                        terrainOceanicityResults[z * sizeX + x] = alreadySet;
                        visitedPoints[z * sizeX + x] = true;
                        continue;
                    } else if (oceanicity > maximumOceanicity) {
                        terrainResults[z * sizeX + x] = alreadySet;
                        terrainOceanicityResults[z * sizeX + x] = alreadySet; //Negative -1 for either of these means this point has already been set. It can be ignored and instead compare against the Oceanicity.
                        result[z * sizeX + x] = seaEnumVal;
                        visitedPoints[z * sizeX + x] = true;
                        continue;
                    }

                    int factoredOceanicity = (int)(oceanicity * oceanicityFac);
                    VectorXZ dist = NewDistortionNoise(worldX, worldZ);
                    VectorXZ distTerrain = ApplyIsotropicDistortionThreshold(dist * terrainDistortionMultiplier, terrainDistortionThreshold,
                        terrainDistortionMultiplier * maxDistortionAmount);

                    genTerraPrety.GetInterpolatedOctaves(LandformLerpMap.WeightsAt(baseX, baseZ, landformWeights), out var lerpedAmps1, out var lerpedTh1);
                    genTerraPrety.GetInterpolatedOctaves(LandformLerpMap.WeightsAt(baseX + halfPixelSize, baseZ, landformWeights), out var lerpedAmps2, out var lerpedTh2);
                    genTerraPrety.GetInterpolatedOctaves(LandformLerpMap.WeightsAt(baseX, baseZ + halfPixelSize, landformWeights), out var lerpedAmps3, out var lerpedTh3);
                    genTerraPrety.GetInterpolatedOctaves(LandformLerpMap.WeightsAt(baseX + halfPixelSize, baseZ + halfPixelSize, landformWeights), out var lerpedAmps4, out var lerpedTh4);

                    LandformLerpMap.WeightsAt(baseX + 0.5f, baseZ + 0.5f, landformWeights); //Need to feed this the chunk data
                    for (int i = 0; i < lerpedAmps.Length; i++) {
                        lerpedAmps[i] = GameMath.BiLerp(lerpedAmps1[i], lerpedAmps2[i], lerpedAmps3[i], lerpedAmps4[i], landformHalfScale * chunkBlockDelta, landformHalfScale * chunkBlockDelta);
                        lerpedTh[i] = GameMath.BiLerp(lerpedTh1[i], lerpedTh2[i], lerpedTh3[i], lerpedTh4[i], landformHalfScale * chunkBlockDelta, landformHalfScale * chunkBlockDelta);
                    }
                    NewNormalizedSimplexFractalNoise.ColumnNoise columnNoise = terrainNoise.ForColumn(verticalNoiseRelativeFrequency, lerpedAmps, lerpedTh, worldX + distTerrain.X, worldZ + distTerrain.Z);
                    double noiseBoundMin = columnNoise.BoundMin;
                    double noiseBoundMax = columnNoise.BoundMax;

                    var threshRegFound = false;
                    var threshOceanicityFound = false;
                    short y = (short)TerraGenConfig.seaLevel; //This is both a remnant of the old full iterative loop from 1 to sealevel, but the only thing that matters here is indeed the status of the Sealevel values.
                    double threshold = 0;
                    double thresholdWithOceanicity = 0;

                    for (int i = 0; i < landformWeights.Length; i++) { //This loop is always needed, as it compounds all possible landform weights. But only has to run once per map point.
                        float weight = landformWeights[i];
                        if (weight == 0) continue;
                        threshold += weight * terrainYThresholds[i][y];
                        thresholdWithOceanicity += weight * terrainYThresholds[i][y + factoredOceanicity];
                    }

                    if (threshold > noiseBoundMin) {
                        double noiseSign = -NormalizedSimplexNoise.NoiseValueCurveInverse(threshold);
                        noiseSign = columnNoise.NoiseSign(y, noiseSign);

                        if (noiseSign <= 0 || !(threshold < noiseBoundMax)) {
                            terrainResults[z * sizeX + x] = y;
                            threshRegFound = true;
                        }
                    }

                    if (thresholdWithOceanicity > noiseBoundMin) {
                        double noiseSign = -NormalizedSimplexNoise.NoiseValueCurveInverse(thresholdWithOceanicity);
                        noiseSign = columnNoise.NoiseSign(y, noiseSign);

                        if (noiseSign <= 0 || !(thresholdWithOceanicity < noiseBoundMax)) {
                            terrainOceanicityResults[z * sizeX + x] = y;
                            threshOceanicityFound = true;
                        }
                    }

                    if (threshRegFound && oceanicity >= softMinimumOceanicity) {
                        pointsToStartFlood.Add(new VectorXZInt { X = x, Z = z }); //If the base Threshold is found, then this is a point that will always be under Sealevel, thus it is Ocean.
                        result[z * sizeX + x] = seaEnumVal;
                    } else if (threshOceanicityFound && oceanicity >= hardMinimumOceanicity) {
                        pointsToExamine.Add(new VectorXZInt { X = x, Z = z }); //If the base Threshold has not been found, but the Oceanicity Threshold has been found, then this is a node of importance to check.
                    } else if (threshRegFound && oceanicity >= ((hardMinimumOceanicity - softMinimumOceanicity) / 2)) {
                        pointsToExamine.Add(new VectorXZInt { X = x, Z = z });
                    }
                }
            }

            if ((pointsToStartFlood.Count <= 0 && pointsToExamine.Count <= 0) || visitedPoints.HasAllSet()) { //If we found no points to start a flood fill, or all points happen to be determined already (IE below or above the oceanicity min and max), can just return early without bothering with a fill!
                return result;
            }

            bool doneWithFill = false; //This is now needed to allow the Second Pass to get hit when the PointsToStartFlood is emptied out, and PointsToExamine is not guaranteed to empty out.
            bool doneSecondPass = false; //Ideally limit this second pass to only actually be JUST a second pass.
            while (!doneWithFill) { //Ideally this only needs to run once for each bunch of isolated start points, then one second pass if there are any 'pointsToExamine' remaining that were not hit. A loop simply to ensure we hit all the Initial Points.
                if (pointsToStartFlood.Count > 0) {
                    var startPoint = pointsToStartFlood.First();
                    pointsToStartFlood.Remove(startPoint);
                    visitedPoints[startPoint.Z * sizeX + startPoint.X] = true;
                    RecursiveFloodFill(startPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);
                } else if (pointsToExamine.Count > 0 && !doneSecondPass) {
                    for (int i = 0; i < pointsToExamine.Count; i++) {
                        var point = pointsToExamine[i];
                        if (point.X == 0 || point.X == sizeX - 1 || point.Z == 0 || point.Z == sizeX - 1) {
                            if (oceanicityResults[point.Z * sizeX + point.X] >= ((hardMinimumOceanicity - softMinimumOceanicity) / 2)) {
                                result[point.Z * sizeX + point.X] = seaEnumVal;
                                pointsToStartFlood.Add(point);
                                visitedPoints[point.Z * sizeX + point.X] = true;
                            }
                        }
                    }
                    doneSecondPass = true;
                } else {
                    doneWithFill = true;
                }
            }

            return result;
        }

        public int[] GenLayerAndAddToCache(int xCoord, int zCoord, int sizeX, int sizeZ) {
            var cacheMe = GenLayer(xCoord, zCoord, sizeX, sizeZ);
            XZ regionCoord = new XZ(xCoord, zCoord);
            coastCache.TryAdd(regionCoord, cacheMe);
            return cacheMe;
        }

        public int[] GetEstimatedYHeights(int xCoord, int zCoord, int sizeX, int sizeZ) {
            //BitArray visitedPoints = new BitArray(sizeX * sizeZ);
            //List<VectorXZInt> pointsToStartFlood = new List<VectorXZInt>();
            //List<VectorXZInt> pointsToExamine = new List<VectorXZInt>();
            //short[] terrainResults = new short[sizeX * sizeZ]; //This will hold the calculations of the TerrainNoise so that it ideally only has to be done once.
            //short[] terrainOceanicityResults = new short[sizeX * sizeZ]; //This is the result of if the TerrainNoise also factored the Oceanicity into it.
            //float[] oceanicityResults = new float[sizeX * sizeZ]; //This holds the actual oceanicity values of each map area
            int[] result = new int[sizeX * sizeZ];
            /*for (int i = 0; i < result.Length; i++) { //Init all arrays to the default state
                //terrainResults[i] = aboveSeaY;
                terrainOceanicityResults[i] = aboveSeaY;
                result[i] = landEnumVal; //(int)EnumCoasts.Land; The Enum is set up so that 0 is Land, 1 is Sea, and 2 is a Ocean Coastline. Quicker to simply just use a constant here instead of converting the Enum? Dunno how it compiles exactly.
            }*/
            //visitedPoints.SetAll(false);
            //pointsToStartFlood.Clear();
            //pointsToExamine.Clear();

            double[] lerpedAmps = new double[terrainGenOctaves];
            double[] lerpedTh = new double[terrainGenOctaves];
            float[] landformWeights = new float[landformLength];
            //int regionX = xCoord / coastalInnerSize;
            //int regionZ = zCoord / coastalInnerSize;

            const int chunksize = GlobalConstants.ChunkSize; //Number of Blocks in a Chunk.
            const float chunkBlockDelta = 1.0f / chunksize;
            var worldHeightM2 = sapi.WorldManager.MapSizeY - 2;

            var genTerraPrety = sapi.ModLoader.GetModSystem<GenTerraPrety>();
            int regionChunkSize = sapi.WorldManager.RegionSize / chunksize; //Number of Chunks in the Region.
            float chunkPixelSize = (landformInnerSize / regionChunkSize); //InnerSize is the map's width/length of 1 region

            int landformHalfScale = TerraGenConfig.landformMapScale / 2;
            double verticalNoiseRelativeFrequency = 0.5 / TerraGenConfig.terrainNoiseVerticalScale;

            for (int x = 0; x < sizeX; x++) { //First Pass: Gather as much of the data points as possible, ideally all of them here. Should only need the Oceanicity and TerrainNoise Results?
                for (int z = 0; z < sizeZ; z++) { //Also set up the initial points if they are valid.
                    float baseX = (x / 2) * chunkPixelSize;
                    float baseZ = (z / 2) * chunkPixelSize;
                    float halfPixelSize = chunkPixelSize / 2;
                    int worldX = (xCoord * TerraGenConfig.landformMapScale) + (x * TerraGenConfig.landformMapScale) + landformHalfScale; //The World Coords at the center of this map pixel.
                    int worldZ = (zCoord * TerraGenConfig.landformMapScale) + (z * TerraGenConfig.landformMapScale) + landformHalfScale;

                    int oceanicity = OceanMap.GetUnpaddedInt((x * OceanMap.InnerSize) / coastalInnerSize, (z * OceanMap.InnerSize) / coastalInnerSize); //X and Z here are the coordinates of the CoastMap's entries for this Region it is building.
                    //oceanicityResults[z * sizeX + x] = oceanicity; //The above translates it into proper coordinates for the OceanMap's scale, which can (and does) differ from the CoastMap's scale.

                    /*if (oceanicity < hardMinimumOceanicity) { //This is comparing the raw values from the map, so it will be from 0 - 255 for Oceanicity.
                        terrainResults[z * sizeX + x] = alreadySet;
                        terrainOceanicityResults[z * sizeX + x] = alreadySet;
                        visitedPoints[z * sizeX + x] = true;
                        continue;
                    } else if (oceanicity > maximumOceanicity) {
                        terrainResults[z * sizeX + x] = alreadySet;
                        terrainOceanicityResults[z * sizeX + x] = alreadySet; //Negative -1 for either of these means this point has already been set. It can be ignored and instead compare against the Oceanicity.
                        result[z * sizeX + x] = seaEnumVal;
                        visitedPoints[z * sizeX + x] = true;
                        continue;
                    }*/

                    int factoredOceanicity = (int)(oceanicity * oceanicityFac);
                    VectorXZ dist = NewDistortionNoise(worldX, worldZ);
                    VectorXZ distTerrain = ApplyIsotropicDistortionThreshold(dist * terrainDistortionMultiplier, terrainDistortionThreshold,
                        terrainDistortionMultiplier * maxDistortionAmount);

                    genTerraPrety.GetInterpolatedOctaves(LandformLerpMap.WeightsAt(baseX, baseZ, landformWeights), out var lerpedAmps1, out var lerpedTh1);
                    genTerraPrety.GetInterpolatedOctaves(LandformLerpMap.WeightsAt(baseX + halfPixelSize, baseZ, landformWeights), out var lerpedAmps2, out var lerpedTh2);
                    genTerraPrety.GetInterpolatedOctaves(LandformLerpMap.WeightsAt(baseX, baseZ + halfPixelSize, landformWeights), out var lerpedAmps3, out var lerpedTh3);
                    genTerraPrety.GetInterpolatedOctaves(LandformLerpMap.WeightsAt(baseX + halfPixelSize, baseZ + halfPixelSize, landformWeights), out var lerpedAmps4, out var lerpedTh4);

                    LandformLerpMap.WeightsAt(baseX + 0.5f, baseZ + 0.5f, landformWeights); //Need to feed this the chunk data
                    for (int i = 0; i < lerpedAmps.Length; i++) {
                        lerpedAmps[i] = GameMath.BiLerp(lerpedAmps1[i], lerpedAmps2[i], lerpedAmps3[i], lerpedAmps4[i], landformHalfScale * chunkBlockDelta, landformHalfScale * chunkBlockDelta);
                        lerpedTh[i] = GameMath.BiLerp(lerpedTh1[i], lerpedTh2[i], lerpedTh3[i], lerpedTh4[i], landformHalfScale * chunkBlockDelta, landformHalfScale * chunkBlockDelta);
                    }
                    NewNormalizedSimplexFractalNoise.ColumnNoise columnNoise = terrainNoise.ForColumn(verticalNoiseRelativeFrequency, lerpedAmps, lerpedTh, worldX + distTerrain.X, worldZ + distTerrain.Z);
                    double noiseBoundMin = columnNoise.BoundMin;
                    double noiseBoundMax = columnNoise.BoundMax;

                    //var threshRegFound = false;
                    //var threshOceanicityFound = false;
                    //short y = (short)TerraGenConfig.seaLevel; //This is both a remnant of the old full iterative loop from 1 to sealevel, but the only thing that matters here is indeed the status of the Sealevel values.
                    
                    for (int y = TerraGenConfig.seaLevel; y <= worldHeightM2; y++) {
                        //double threshold = 0;
                        double thresholdWithOceanicity = 0;

                        for (int i = 0; i < landformWeights.Length; i++) { //This loop is always needed, as it compounds all possible landform weights. But only has to run once per map point.
                            float weight = landformWeights[i];
                            if (weight == 0) continue;
                            //threshold += weight * terrainYThresholds[i][y];
                            thresholdWithOceanicity += weight * terrainYThresholds[i][y + factoredOceanicity];
                        }

                        /*if (threshold > noiseBoundMin) {
                            double noiseSign = -NormalizedSimplexNoise.NoiseValueCurveInverse(threshold);
                            noiseSign = columnNoise.NoiseSign(y, noiseSign);

                            if (noiseSign <= 0 || !(threshold < noiseBoundMax)) {
                                terrainResults[z * sizeX + x] = y;
                                threshRegFound = true;
                            }
                        }*/

                        if (thresholdWithOceanicity > noiseBoundMin) {
                            double noiseSign = -NormalizedSimplexNoise.NoiseValueCurveInverse(thresholdWithOceanicity);
                            noiseSign = columnNoise.NoiseSign(y, noiseSign);

                            if (noiseSign <= 0 || !(thresholdWithOceanicity < noiseBoundMax)) {
                                result[z * sizeX + x] = y;
                                //threshOceanicityFound = true;
                                break;
                            }
                        }

                        /*if (threshRegFound && oceanicity >= softMinimumOceanicity) {
                            pointsToStartFlood.Add(new VectorXZInt { X = x, Z = z }); //If the base Threshold is found, then this is a point that will always be under Sealevel, thus it is Ocean.
                            result[z * sizeX + x] = seaEnumVal;
                        } else if (threshOceanicityFound && oceanicity >= hardMinimumOceanicity) {
                            pointsToExamine.Add(new VectorXZInt { X = x, Z = z }); //If the base Threshold has not been found, but the Oceanicity Threshold has been found, then this is a node of importance to check.
                        } else if (threshRegFound && oceanicity >= ((hardMinimumOceanicity - softMinimumOceanicity) / 2)) {
                            pointsToExamine.Add(new VectorXZInt { X = x, Z = z });
                        }*/
                    }
                }
            }

            /*if ((pointsToStartFlood.Count <= 0 && pointsToExamine.Count <= 0) || visitedPoints.HasAllSet()) { //If we found no points to start a flood fill, or all points happen to be determined already (IE below or above the oceanicity min and max), can just return early without bothering with a fill!
                return result;
            }

            bool doneWithFill = false; //This is now needed to allow the Second Pass to get hit when the PointsToStartFlood is emptied out, and PointsToExamine is not guaranteed to empty out.
            bool doneSecondPass = false; //Ideally limit this second pass to only actually be JUST a second pass.
            while (!doneWithFill) { //Ideally this only needs to run once for each bunch of isolated start points, then one second pass if there are any 'pointsToExamine' remaining that were not hit. A loop simply to ensure we hit all the Initial Points.
                if (pointsToStartFlood.Count > 0) {
                    var startPoint = pointsToStartFlood.First();
                    pointsToStartFlood.Remove(startPoint);
                    visitedPoints[startPoint.Z * sizeX + startPoint.X] = true;
                    RecursiveFloodFill(startPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);
                } else if (pointsToExamine.Count > 0 && !doneSecondPass) {
                    for (int i = 0; i < pointsToExamine.Count; i++) {
                        var point = pointsToExamine[i];
                        if (point.X == 0 || point.X == sizeX - 1 || point.Z == 0 || point.Z == sizeX - 1) {
                            if (oceanicityResults[point.Z * sizeX + point.X] >= ((hardMinimumOceanicity - softMinimumOceanicity) / 2)) {
                                result[point.Z * sizeX + point.X] = seaEnumVal;
                                pointsToStartFlood.Add(point);
                                visitedPoints[point.Z * sizeX + point.X] = true;
                            }
                        }
                    }
                    doneSecondPass = true;
                } else {
                    doneWithFill = true;
                }
            }*/

            return result;
        }

        private void RecursiveFloodFill(VectorXZInt workingPoint, int sizeX, short[] terrainOceanicityResults, ref BitArray visitedPoints, ref List<VectorXZInt> pointsToStartFlood, ref List<VectorXZInt> pointsToExamine, ref int[] result) {
            TestUpFill(workingPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);
            TestRightFill(workingPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);
            TestDownFill(workingPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);
            TestLeftFill(workingPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);
        }

        //"Up" is one step in the Negative Z direction
        private void TestUpFill(VectorXZInt workingPoint, int sizeX, short[] terrainOceanicityResults, ref BitArray visitedPoints, ref List<VectorXZInt> pointsToStartFlood, ref List<VectorXZInt> pointsToExamine, ref int[] result) {
            if (workingPoint.Z - 1 < 0) {
                return; //One step in this direction does not exist. Just return early, good to go!
            }

            workingPoint.Z -= 1;
            if (visitedPoints[workingPoint.Z * sizeX + workingPoint.X]) {
                return; //If this point has already been visited, no need to go in this direction either! Return early.
            }

            //Actually do the tests and setting of Result here!
            TestPointDuringFill(workingPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);

            if (result[workingPoint.Z * sizeX + workingPoint.X] != landEnumVal) {
                result[(workingPoint.Z + 1) * sizeX + workingPoint.X] = seaEnumVal;
                RecursiveFloodFill(workingPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);
            } else {
                result[workingPoint.Z * sizeX + workingPoint.X] = coastEnumVal;
            }
        }

        //"Right" is one step in the Positive X direction
        private void TestRightFill(VectorXZInt workingPoint, int sizeX, short[] terrainOceanicityResults, ref BitArray visitedPoints, ref List<VectorXZInt> pointsToStartFlood, ref List<VectorXZInt> pointsToExamine, ref int[] result) {
            if (workingPoint.X + 1 >= sizeX) {
                return; //One step in this direction does not exist. Just return early, good to go!
            }

            workingPoint.X += 1;
            if (visitedPoints[workingPoint.Z * sizeX + workingPoint.X]) {
                return; //If this point has already been visited, no need to go in this direction either! Return early.
            }

            //Actually do the tests and setting of Result here!
            TestPointDuringFill(workingPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);

            if (result[workingPoint.Z * sizeX + workingPoint.X] != landEnumVal) {
                result[workingPoint.Z * sizeX + (workingPoint.X - 1)] = seaEnumVal;
                RecursiveFloodFill(workingPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);
            } else {
                result[workingPoint.Z * sizeX + workingPoint.X] = coastEnumVal;
            }
        }

        //"Down" is one step in the Positive Z direction
        private void TestDownFill(VectorXZInt workingPoint, int sizeX, short[] terrainOceanicityResults, ref BitArray visitedPoints, ref List<VectorXZInt> pointsToStartFlood, ref List<VectorXZInt> pointsToExamine, ref int[] result) {
            if (workingPoint.Z + 1 >= sizeX) {
                return; //One step in this direction does not exist. Just return early, good to go!
            }

            workingPoint.Z += 1;
            if (visitedPoints[workingPoint.Z * sizeX + workingPoint.X]) {
                return; //If this point has already been visited, no need to go in this direction either! Return early.
            }

            //Actually do the tests and setting of Result here!
            TestPointDuringFill(workingPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);

            if (result[workingPoint.Z * sizeX + workingPoint.X] != landEnumVal) {
                result[(workingPoint.Z - 1) * sizeX + workingPoint.X] = seaEnumVal;
                RecursiveFloodFill(workingPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);
            } else {
                result[workingPoint.Z * sizeX + workingPoint.X] = coastEnumVal;
            }
        }

        //"Left" is one step in the Negative X direction
        private void TestLeftFill(VectorXZInt workingPoint, int sizeX, short[] terrainOceanicityResults, ref BitArray visitedPoints, ref List<VectorXZInt> pointsToStartFlood, ref List<VectorXZInt> pointsToExamine, ref int[] result) {
            if (workingPoint.X - 1 < 0) {
                return; //One step in this direction does not exist. Just return early, good to go!
            }

            workingPoint.X -= 1;
            if (visitedPoints[workingPoint.Z * sizeX + workingPoint.X]) {
                return; //If this point has already been visited, no need to go in this direction either! Return early.
            }

            //Actually do the tests and setting of Result here!
            TestPointDuringFill(workingPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);

            if (result[workingPoint.Z * sizeX + workingPoint.X] != landEnumVal) {
                result[workingPoint.Z * sizeX + (workingPoint.X + 1)] = seaEnumVal;
                RecursiveFloodFill(workingPoint, sizeX, terrainOceanicityResults, ref visitedPoints, ref pointsToStartFlood, ref pointsToExamine, ref result);
            } else {
                result[workingPoint.Z * sizeX + workingPoint.X] = coastEnumVal;
            }
        }

        private void TestPointDuringFill(VectorXZInt workingPoint, int sizeX, short[] terrainOceanicityResults, ref BitArray visitedPoints, ref List<VectorXZInt> pointsToStartFlood, ref List<VectorXZInt> pointsToExamine, ref int[] result) {
            pointsToStartFlood.Remove(workingPoint);
            pointsToExamine.Remove(workingPoint);
            visitedPoints[workingPoint.Z * sizeX + workingPoint.X] = true;

            var oceanicityYHeight = terrainOceanicityResults[workingPoint.Z * sizeX + workingPoint.X];
            if (oceanicityYHeight > 0) {
                result[workingPoint.Z * sizeX + workingPoint.X] = coastEnumVal;
            }
        }

        private double[] scaleAdjustedFreqs(double[] vs, float horizontalScale) {
            for (int i = 0; i < vs.Length; i++) {
                vs[i] /= horizontalScale;
            }

            return vs;
        }

        struct VectorXZInt {
            public int X, Z;
        }

        //Below here has been copied and reused from GenTerra, to generate the same kind of distortion noise that it uses to build the world.
        struct VectorXZ {
            public double X, Z;
            public static VectorXZ operator *(VectorXZ a, double b) => new VectorXZ { X = a.X * b, Z = a.Z * b };
        }

        // Closesly matches the old two-noise distortion in a given seed, but is more fair to all angles.
        VectorXZ NewDistortionNoise(double worldX, double worldZ) {
            double noiseX = worldX / 400.0;
            double noiseZ = worldZ / 400.0;
            SimplexNoise.NoiseFairWarpVector(distort2dx, distort2dz, noiseX, noiseZ, out double distX, out double distZ);
            return new VectorXZ { X = distX, Z = distZ };
        }

        // Cuts off the distortion in a circle rather than a square.
        // Between this and the new distortion noise, this makes the bigger difference.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        VectorXZ ApplyIsotropicDistortionThreshold(VectorXZ dist, double threshold, double maximum) {
            double distMagnitudeSquared = dist.X * dist.X + dist.Z * dist.Z;
            double thresholdSquared = threshold * threshold;
            if (distMagnitudeSquared <= thresholdSquared) dist.X = dist.Z = 0;
            else {
                // `slide` is 0 to 1 between `threshold` and `maximum` (input vector magnitude)
                double baseCurve = (distMagnitudeSquared - thresholdSquared) / distMagnitudeSquared;
                double maximumSquared = maximum * maximum;
                double baseCurveReciprocalAtMaximum = maximumSquared / (maximumSquared - thresholdSquared);
                double slide = baseCurve * baseCurveReciprocalAtMaximum;

                // Let  `slide` be smooth to start.
                slide *= slide;

                // `forceDown` needs to make `dist` zero at `threshold`
                // and `expectedOutputMaximum` at `maximum`.
                double expectedOutputMaximum = maximum - threshold;
                double forceDown = slide * (expectedOutputMaximum / maximum);

                dist *= forceDown;
            }
            return dist;
        }
    }
}
