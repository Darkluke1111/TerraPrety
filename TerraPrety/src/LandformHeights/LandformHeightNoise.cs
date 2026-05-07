using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace TerraPrety.LandformHeights {

    public struct RequiredHeightPoints {
        public int x;
        public int z;
        public int radius;
        public double minHeight;
        public double maxHeight;
        public double centerHeight;

        public RequiredHeightPoints(int x, int z, int radius, double minHeight, double maxHeight) {
            this.x = (x / TerraGenConfig.landformMapScale);
            this.z = (z / TerraGenConfig.landformMapScale);
            this.radius = (radius / TerraGenConfig.landformMapScale);
            this.minHeight = minHeight;
            this.maxHeight = maxHeight;
            this.centerHeight = minHeight + ((maxHeight - minHeight) / 2);
        }

        public bool IsWithinRange(int X, int Z, int range) {
            return X > (x - range) && X < (x + range) && Z > (z - range) && Z < (z + range);
        }
    }

    public class LandformHeightNoise : NoiseBase {

        public static LandformsWorldProperty landforms;
        public static LandformsHeightsWorldProperty landformsHeights;

        protected WeightedNormalizedSimplexNoise heightNoise;
        protected List<ForceLandform> forcedLandforms;
        protected WorldGenConfig config;
        protected int fallbackParentLandformID;
        public float scale;
        public float oceanicityFactor;
        private ICoreServerAPI sapi;

        public const int significantDigitMult = 10000;

        // A seperate instance of each of these for every chunkdbthread and it's workers
        [ThreadStatic] static int xPos;
        [ThreadStatic] static int zPos;
        [ThreadStatic] static int heightMapRegionXSize;
        [ThreadStatic] static int heightMapRegionZSize;
        [ThreadStatic] public static int[] heightMapValues;
        [ThreadStatic] static double[] weightTmp;

        private readonly long mapGenSeed;

        private float[] threshForOceanicityComp;
        private float[] oceanicityCompMults;
        private float[] oceanicityCompFlats;

        public LandformHeightNoise(long seed, ICoreServerAPI api, float scale, WorldGenConfig config) : base(seed) {
            this.scale = scale;
            this.config = config;
            this.oceanicityFactor = ((float)(api.WorldManager.MapSizeY - 64) / (float)256) * 0.33333f; //the -64 is to account for the shifting of the whole world downwards by 64 to fit more Mountain room above.
            forcedLandforms = new List<ForceLandform>();
            sapi = api;

            // Move the NoiseBase constructor's mapGenSeed creation here so different threads can get the seed rng without risking modifying the seed creation at the same time
            this.mapGenSeed = seed;
            this.mapGenSeed *= seed * 6364136223846793005L + 1442695040888963407L;
            this.mapGenSeed += 1L;
            this.mapGenSeed *= seed * 6364136223846793005L + 1442695040888963407L;
            this.mapGenSeed += 2L;
            this.mapGenSeed *= seed * 6364136223846793005L + 1442695040888963407L;
            this.mapGenSeed += 3L;

            int hOctaves = this.config.heightMapOctaves;
            float hScale = this.config.heightMapNoiseScale;
            float hPersistance = this.config.heightMapPersistance;
            heightNoise = new WeightedNormalizedSimplexNoise(hOctaves, 1 / hScale, hPersistance, seed + 53247, this.config.radiusMultOutwardsForSmoothing, scale, config.chanceForMidZone, config.midHeightKeys, config.midHeightValues, config.targetMidLevel, config.lowThreshForMidZone);

            threshForOceanicityComp = config.heightThresholdsForOceanicityComp;
            oceanicityCompMults = config.heightMultsAtThresholdsForOceanicityComp;
            oceanicityCompFlats = config.heightFlatsAtThresholdsForOceanicityComp;

            LoadLandforms(api);
        }

        public static void LoadLandforms(ICoreServerAPI api) {
            IAsset asset = api.Assets.Get("worldgen/landforms.json");
            landforms = asset.ToObject<LandformsWorldProperty>();

            IAsset heightsProperty = api.Assets.Get("terraprety:worldgen/landformheights.json");
            landformsHeights = heightsProperty.ToObject<LandformsHeightsWorldProperty>();

            int quantityMutations = 0;
            landformsHeights.LandformHeightsByIndex = new LandformGenHeight[landforms.Variants.Length];

            for (int i = 0; i < landforms.Variants.Length; i++) {
                LandformVariant variant = landforms.Variants[i];
                variant.index = i;
                variant.Init(api.WorldManager, i);

                LandformGenHeight varHeight = landformsHeights.Variants.FirstOrDefault(h => h.Code.Path == variant.Code.Path, new LandformGenHeight() { Code = new AssetLocation() });
                varHeight.Init(i);
                landformsHeights.LandformHeightsByIndex[i] = varHeight; //Adding these in here since Mutations likely don't need separate heights from the parents?

                if (variant.Mutations != null) {
                    quantityMutations += variant.Mutations.Length;
                }
            }

            landforms.LandFormsByIndex = new LandformVariant[quantityMutations + landforms.Variants.Length];

            // Mutations get indices after the parent ones
            for (int i = 0; i < landforms.Variants.Length; i++) {
                landforms.LandFormsByIndex[i] = landforms.Variants[i];
            }

            int nextIndex = landforms.Variants.Length;
            for (int i = 0; i < landforms.Variants.Length; i++) {
                LandformVariant variant = landforms.Variants[i];
                if (variant.Mutations != null) {
                    for (int j = 0; j < variant.Mutations.Length; j++) {
                        LandformVariant variantMut = variant.Mutations[j];

                        if (variantMut.TerrainOctaves == null) {
                            variantMut.TerrainOctaves = variant.TerrainOctaves;
                        }
                        if (variantMut.TerrainOctaveThresholds == null) {
                            variantMut.TerrainOctaveThresholds = variant.TerrainOctaveThresholds;
                        }
                        if (variantMut.TerrainYKeyPositions == null) {
                            variantMut.TerrainYKeyPositions = variant.TerrainYKeyPositions;
                        }
                        if (variantMut.TerrainYKeyThresholds == null) {
                            variantMut.TerrainYKeyThresholds = variant.TerrainYKeyThresholds;
                        }


                        landforms.LandFormsByIndex[nextIndex] = variantMut;
                        variantMut.Init(api.WorldManager, nextIndex);
                        nextIndex++;
                    }
                }
            }
        }

        public void AddForcedLandform(ForceLandform forced) {
            forcedLandforms.Add(forced);
        }

        public void SetForcedHeightPoints() {
            List<RequiredHeightPoints> reqHeights = new List<RequiredHeightPoints>();
            foreach (var forcedLand in forcedLandforms) {
                int heightReqIndex = -1;
                var list = landforms.LandFormsByIndex;
                for (int i = 0; i < list.Length; i++) {
                    if (list[i].Code.Path == forcedLand.LandformCode) {
                        heightReqIndex = i;
                        break;
                    }
                }

                LandformGenHeight heights;
                if (heightReqIndex != -1) {
                    heights = landformsHeights.LandformHeightsByIndex[heightReqIndex];
                } else {
                    heights = new LandformGenHeight();
                }

                var modifiedX = forcedLand.CenterPos.X + forcedLand.Radius;
                var modifiedZ = forcedLand.CenterPos.Z + forcedLand.Radius;

                reqHeights.Add(new RequiredHeightPoints(modifiedX, modifiedZ, forcedLand.Radius, heights.minHeight, heights.maxHeight));
            }

            heightNoise.SetRequiredPoints(reqHeights);
        }

        public void FindForcedLandformID() {
            for (int i = 0; i < landforms.LandFormsByIndex.Length; i++) {
                if (landforms.LandFormsByIndex[i].Code == config.fallbackParentLandformCode) {
                    fallbackParentLandformID = i;
                    return;
                }
            }
            fallbackParentLandformID = 0; //This will at least ensure it is set to _something_ and it will just take the first entry. In the case of a typo or the like.
        }

        public int GetLandformIndexAt(int unscaledXpos, int unscaledZpos, int temp, int rain) {
            int noiseSizeLandform = sapi.ModLoader.GetModSystem<GenMaps>().noiseSizeLandform;

            int regionX = unscaledXpos / (noiseSizeLandform - TerraGenConfig.landformMapPadding);
            int regionZ = unscaledZpos / (noiseSizeLandform - TerraGenConfig.landformMapPadding);

            var region = sapi.WorldManager.GetMapRegion(regionX, regionZ);

            float xpos = unscaledXpos / scale;
            float zpos = unscaledZpos / scale;

            int xposInt = (int)xpos;
            int zposInt = (int)zpos;

            //TerraGenConfig.landFormSmoothingRadius = 0;
            //TerraGenConfig.landformMapPadding = 1;
            //TerraGenConfig.terrainNoiseVerticalScale = 2;

            int parentIndex = GetParentLandformIndexAt(xposInt, zposInt, unscaledXpos, unscaledZpos, temp, rain);

            LandformVariant[] mutations = landforms.Variants[parentIndex].Mutations;
            if (mutations != null && mutations.Length > 0) {
                long mutationSeed = ThreadLocalPositionSeed(mapGenSeed, unscaledXpos / 2, unscaledZpos / 2);
                float chance = ThreadLocalNextInt(ref mutationSeed, mapGenSeed, 101) / 100f;

                for (int i = 0; i < mutations.Length; i++) {
                    LandformVariant variantMut = mutations[i];

                    if (variantMut.UseClimateMap) {
                        int distRain = rain - GameMath.Clamp(rain, variantMut.MinRain, variantMut.MaxRain);
                        double distTemp = temp - GameMath.Clamp(temp, variantMut.MinTemp, variantMut.MaxTemp);
                        if (distRain != 0 || distTemp != 0) continue;
                    }

                    chance -= mutations[i].Chance;
                    if (chance <= 0) {
                        return mutations[i].index;
                    }
                }
            }
            return parentIndex;
        }


        public int GetParentLandformIndexAt(int xpos, int zpos, int unscaledXpos, int unscaledZpos, int temp, int rain) {
            long currentSeed = ThreadLocalPositionSeed(mapGenSeed, xpos, zpos);

            double weightSum = 0;
            double heightAtPoint = heightNoise.Height(unscaledXpos, unscaledZpos);

            SaveValueToHeightmap(heightAtPoint);

            // Thread local landform variant weights
            int variantCount = landforms.Variants.Length;
            double[] weights = weightTmp;
            if (weights == null || weights.Length < variantCount) {
                weights = new double[variantCount];
                weightTmp = weights;
            }

            int i;
            for (i = 0; i < variantCount; i++) {
                double weight = landforms.Variants[i].Weight;

                if (heightAtPoint < landformsHeights.LandformHeightsByIndex[i].minHeight || heightAtPoint > landformsHeights.LandformHeightsByIndex[i].maxHeight) {
                    weight = 0;
                }
                if (weight != 0 && landforms.Variants[i].UseClimateMap) {
                    int distRain = rain - GameMath.Clamp(rain, landforms.Variants[i].MinRain, landforms.Variants[i].MaxRain);
                    double distTemp = temp - GameMath.Clamp(temp, landforms.Variants[i].MinTemp, landforms.Variants[i].MaxTemp);
                    if (distRain != 0 || distTemp != 0) weight = 0;
                }

                weights[i] = weight;
                weightSum += weight;
            }

            if (weightSum <= 0) {
                return landforms.Variants[fallbackParentLandformID].index;
            }

            double rand = weightSum * ThreadLocalNextInt(ref currentSeed, mapGenSeed, 10000) / 10000.0;

            for (i = 0; i < variantCount; i++) {
                rand -= weights[i];
                if (rand <= 0) return landforms.Variants[i].index;
            }

            // Floating-point drift can leave rand still above 0 after subtracting every weight
            // If that happens use the last variant since i is now 1 past the size of landforms.Variants
            return landforms.Variants[variantCount - 1].index;
        }

        // Move NoiseBase.InitPositionSeed's currentSeed modifications here so different threads can get the seed rng without risking modifying the seed at the same time
        private static long ThreadLocalPositionSeed(long mapGenSeed, int xPos, int zPos) {
            long seed = mapGenSeed;
            seed *= seed * 6364136223846793005L + 1442695040888963407L; seed += xPos;
            seed *= seed * 6364136223846793005L + 1442695040888963407L; seed += zPos;
            seed *= seed * 6364136223846793005L + 1442695040888963407L; seed += xPos;
            seed *= seed * 6364136223846793005L + 1442695040888963407L; seed += zPos;
            return seed;
        }

        // Move NoiseBase.NextInt's currentSeed modifications here so different threads can get the seed rng without risking modifying the seed at the same time
        private static int ThreadLocalNextInt(ref long currentSeed, long mapGenSeed, int max) {
            int num = (int)((currentSeed >> 24) % (long)max);
            if (num < 0)
            {
                num += max;
            }

            currentSeed *= currentSeed * 6364136223846793005L + 1442695040888963407L;
            currentSeed += mapGenSeed;
            return num;
        }

        public float GetCompValueForOceanicity(int worldX, int worldZ, float oceanicity) { //This is a mess I'll clean up later. Oof.
            if (oceanicity <= 16.6663f) {
                return 16.6664f;
            }

            var heightAtX = worldX / TerraGenConfig.landformMapScale;
            var heightAtZ = worldZ / TerraGenConfig.landformMapScale;
            var height = heightNoise.Height((int)heightAtX, (int)heightAtZ);

            var thresholdIndex = GetHeightThresholdIndex(height);
            var compValue = (float)(height * oceanicityCompMults[thresholdIndex]) * oceanicityFactor;
            return (compValue + oceanicityCompFlats[thresholdIndex]);
        }

        public int GetHeightThresholdIndex(double height) { //This will find a valid threshold or simply return the last one.
            var thresholds = threshForOceanicityComp;
            var prevThreshold = 0.0f;
            int i;

            for (i = 0; i < thresholds.Length; i++) {
                if (height > prevThreshold && height <= thresholds[i]) {
                    return i;
                }
                prevThreshold = thresholds[i];
            }

            return 0;
        }

        public void PrepareForNewHeightmap(int xCoord, int zCoord, int sizeX, int sizeZ) {
            heightMapValues = null;
            heightMapValues = new int[sizeX * sizeZ];
            heightMapRegionXSize = sizeX;
            heightMapRegionZSize = sizeZ;
            xPos = 0;
            zPos = 0;
        }

        public void SaveValueToHeightmap(double height) {
            heightMapValues[zPos * heightMapRegionXSize + xPos] = (int)(height * significantDigitMult);

            zPos++;
            if (zPos >= heightMapRegionZSize) {
                zPos = 0;
                xPos++;
            }
        }

        public IntDataMap2D GetHeightData() {
            var pad = TerraGenConfig.landformMapPadding;
            var landformScale = sapi.WorldManager.RegionSize / TerraGenConfig.landformMapScale;
            int[] heightCopy = new int[heightMapValues.Length];
            heightMapValues.CopyTo(heightCopy, 0);

            return new IntDataMap2D {
                Data = heightCopy,
                Size = landformScale + 2 * pad,
                TopLeftPadding = pad,
                BottomRightPadding = pad
            };
        }
    }
}
