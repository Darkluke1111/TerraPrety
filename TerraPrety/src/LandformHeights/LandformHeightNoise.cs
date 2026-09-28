using MapLayer;
using TerraPrety.Rivers;
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

        public int DistanceTo(int X, int Z) {
            var xdiff = (x - X);
            var zdiff = (z - Z);
            return (int)Math.Sqrt((xdiff * xdiff) + (zdiff * zdiff));
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
        [ThreadStatic] public static int[] mountainRangeMapValues;
        [ThreadStatic] public static int[] upliftMapValues;
        [ThreadStatic] static double[] weightTmp;

        private readonly long mapGenSeed;

        private NormalizedSimplexNoise mountainRangeWobbleX;
        private NormalizedSimplexNoise mountainRangeWobbleZ;
        private float mountainRangeWobbleIntensity;

        private NormalizedSimplexNoise inlandApertureMaskNoise;
        private NormalizedSimplexNoise coastalApertureMaskNoise;

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
            heightNoise = new WeightedNormalizedSimplexNoise(hOctaves, 1 / hScale, hPersistance, seed + 53247, this.config.radiusMultOutwardsForSmoothing, scale, config.chanceForMidZone, config.midHeightKeys, config.midHeightValues, config.targetMidLevel, config.lowThreshForMidZone, config.inlandMountainRangeScale, config.inlandMountainRangeKeys, config.inlandMountainRangeValues, config.mountainRangesPullsHeightMapTowards);

            float inlandMountainRangeWobbleScale = config.inlandMountainRangeWobbleScale * TerraGenConfig.landformMapScale;
            mountainRangeWobbleIntensity = config.inlandMountainRangeWobbleIntensity * TerraGenConfig.landformMapScale;
            mountainRangeWobbleX = NormalizedSimplexNoise.FromDefaultOctaves(config.inlandMountainRangeWobbleOctaves, 1 / inlandMountainRangeWobbleScale, config.inlandMountainRangeWobblePersistence, seed + 7411);
            mountainRangeWobbleZ = NormalizedSimplexNoise.FromDefaultOctaves(config.inlandMountainRangeWobbleOctaves, 1 / inlandMountainRangeWobbleScale, config.inlandMountainRangeWobblePersistence, seed + 7433);

            inlandApertureMaskNoise = NormalizedSimplexNoise.FromDefaultOctaves(1, 1 / (config.inlandMountainRangeApertureMaskScale * TerraGenConfig.landformMapScale), 0.9, seed + 5101);
            coastalApertureMaskNoise = NormalizedSimplexNoise.FromDefaultOctaves(1, 1 / (config.coastalMountainRangeApertureMaskScale * TerraGenConfig.landformMapScale), 0.9, seed + 5119);

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

        public void BorrowHeightMapReference(ref WeightedNormalizedSimplexNoise sharedHeightNoise) {
            sharedHeightNoise = heightNoise;
        }

        public int GetLandformIndexAt(int unscaledXpos, int unscaledZpos, double mountainRangeOpacity, int temp, int rain) {
            /*int noiseSizeLandform = sapi.ModLoader.GetModSystem<GenMaps>().noiseSizeLandform;

            int regionX = unscaledXpos / (noiseSizeLandform - TerraGenConfig.landformMapPadding); //Why did I have this again...? Huh.
            int regionZ = unscaledZpos / (noiseSizeLandform - TerraGenConfig.landformMapPadding);

            var region = sapi.WorldManager.GetMapRegion(regionX, regionZ);*/

            float xpos = unscaledXpos / scale;
            float zpos = unscaledZpos / scale;

            int xposInt = (int)xpos;
            int zposInt = (int)zpos;

            //TerraGenConfig.landFormSmoothingRadius = 0;
            //TerraGenConfig.landformMapPadding = 1;
            //TerraGenConfig.terrainNoiseVerticalScale = 2;

            int parentIndex = GetParentLandformIndexAt(xposInt, zposInt, unscaledXpos, unscaledZpos, mountainRangeOpacity, temp, rain);

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


        public int GetParentLandformIndexAt(int xpos, int zpos, int unscaledXpos, int unscaledZpos, double mountainRangeOpacity, int temp, int rain) {
            long currentSeed = ThreadLocalPositionSeed(mapGenSeed, xpos, zpos);

            double weightSum = 0;
            var coastTuple = this.CoastalMapLoweredHeight(unscaledXpos, unscaledZpos);
            double coastHeight = coastTuple.Item1;
            double forcedPointWeight = coastTuple.Item2;

            // Don't lift within forced landforms
            /*double heightAtPoint;
            if (heightNoise.IsInForcedLandform(unscaledXpos, unscaledZpos))
                heightAtPoint = (double)coastHeight;
            else*/
            double mountainAndForcedPoint = mountainRangeOpacity - forcedPointWeight;
            double heightAtPoint = (double)heightNoise.LiftTowardMountainRangeTargetHeight(coastHeight, mountainAndForcedPoint);

            int upliftBlocks = 0;
            if (mountainAndForcedPoint > 0.5) { //Limit it to the upper half of the mountain height, so that it is a lot less likely to uplift just random flat regions.
                upliftBlocks = (int)((mountainAndForcedPoint - 0.5) * (-64 * 2)); //Technically this can be simplified, but I want to leave it at -64 specifically because this is a hard limit. It CANNOT end up going above 64 or it's going to go higher then the game world.
            }

            this.SaveValueToHeightmap(heightAtPoint, mountainRangeOpacity, upliftBlocks);

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

        public double HeightNoiseHeight(int unscaledXpos, int unscaledZpos)
            => (this.heightNoise.Height(unscaledXpos, unscaledZpos).Item1);

        public double FinalMountainRangeMask(int unscaledXpos, int unscaledZpos)
        {
            this.MountainRangeWobble(unscaledXpos, unscaledZpos, out int wobbledX, out int wobbledZ);

            // Blend the inland mountain range with the coastal mountain range to give the finished mountain range
            double mountainRange = Math.Max(
                this.InlandMountainRangeRaw(wobbledX, wobbledZ) * this.InlandApertureMask(unscaledXpos, unscaledZpos),
                this.CoastalMountainRangeRaw(wobbledX, wobbledZ) * this.CoastalApertureMask(unscaledXpos, unscaledZpos));

            return mountainRange * this.OceanFadeMountainRange(unscaledXpos, unscaledZpos);
        }

        private double OceanFadeMountainRange(int unscaledXpos, int unscaledZpos)
        {
            MapLayerOceansSmooth ocean = MapLayerOceansSmooth.Instance;
            if (ocean == null)
                return 1.0;

            int oceanX = unscaledXpos * TerraGenConfig.landformMapScale / TerraGenConfig.oceanMapScale;
            int oceanZ = unscaledZpos * TerraGenConfig.landformMapScale / TerraGenConfig.oceanMapScale;

            double continentalPosition = ocean.ContinentalPosition(oceanX, oceanZ);
            double fadeStart = config.mountainRangeFadeStartPositionInContinent;
            double fadeEnd = config.mountainRangeFadeEndPositionInContinent;

            double fade = GameMath.Clamp((continentalPosition - fadeStart) / (fadeEnd - fadeStart), 0.0, 1.0);

            return 1.0 - GameMath.Clamp(fade, 0.0, 1.0);
        }

        private double InlandMountainRangeRaw(int x, int z) => this.heightNoise.InlandMountainRangeMaskValue(x, z);

        private double CoastalMountainRangeRaw(int x, int z)
        {
            MapLayerOceansSmooth ocean = MapLayerOceansSmooth.Instance;
            if (ocean == null)
                return 0.0;

            int oceanX = x * TerraGenConfig.landformMapScale / TerraGenConfig.oceanMapScale;
            int oceanZ = z * TerraGenConfig.landformMapScale / TerraGenConfig.oceanMapScale;
            double continentalPosition = ocean.ContinentalPosition(oceanX, oceanZ);

            double coastalMountainRangeBand = 1.0 - (Math.Abs(continentalPosition - config.coastalMountainRangeBandPositionInContinent) / config.coastalMountainRangeBandBaseWidth);
            if (coastalMountainRangeBand <= 0.0)
                return 0.0;

            return NoiseRemapper.GetValueAt(coastalMountainRangeBand, config.coastalMountainRangeKeys, config.coastalMountainRangeValues);
        }

        private void MountainRangeWobble(int x, int z, out int wobbledX, out int wobbledZ)
        {
            wobbledX = x + (int)(mountainRangeWobbleIntensity * mountainRangeWobbleX.Noise(x, z));
            wobbledZ = z + (int)(mountainRangeWobbleIntensity * mountainRangeWobbleZ.Noise(x, z));
        }

        private double InlandApertureMask(int x, int z) => ApertureMask(inlandApertureMaskNoise.Noise(x, z), config.inlandMountainRangeApertureMaskThreshold, config.inlandMountainRangeApertureMaskSharpness);
        private double CoastalApertureMask(int x, int z) => ApertureMask(coastalApertureMaskNoise.Noise(x, z), config.coastalMountainRangeApertureMaskThreshold, config.coastalMountainRangeApertureMaskSharpness);

        private static double ApertureMask(double noise, double threshold, double ApertureSharpness)
        {
            if (ApertureSharpness <= 0.0)
            {
                if (noise >= threshold)
                    return (double)1.0;
                else
                    return (double)0.0;
            }

            // Smoothstep in the mask
            double step = GameMath.Clamp((noise - (threshold - ApertureSharpness)) / (2.0 * ApertureSharpness), 0.0, 1.0);
            return step * step * (3.0 - (2.0 * step));
        }

        public (double, double) CoastalMapLoweredHeight(int unscaledXpos, int unscaledZpos)
        {
            var heightTuple = heightNoise.Height(unscaledXpos, unscaledZpos);
            double heightNoiseHeight = heightTuple.Item1;
            double forcedPointWeight = heightTuple.Item2;

            // Only lower, don't raise
            if (heightNoiseHeight <= config.coastTargetLandformHeight)
                return heightTuple;

            MapLayerOceansSmooth ocean = MapLayerOceansSmooth.Instance;
            if (ocean == null) // World startup race guard
                return heightTuple;

            int oceanX = unscaledXpos * TerraGenConfig.landformMapScale / TerraGenConfig.oceanMapScale;
            int oceanZ = unscaledZpos * TerraGenConfig.landformMapScale / TerraGenConfig.oceanMapScale;

            double rawCoastOpacity = ocean.CoastOpacity(oceanX, oceanZ);

            // 0 is no coast, 1 is full coast
            double normalizedCoastOpacity = Math.Clamp(
                (rawCoastOpacity - config.coastMinOpacity) / (config.coastFullOpacity - config.coastMinOpacity),
                0.0,
                1.0);

            double coastAndForcedFactor = normalizedCoastOpacity - forcedPointWeight;

            if (coastAndForcedFactor <= 0.0) {
                return heightTuple;
            } else {
                // Lower the landform height down towards the coast target
                return (GameMath.Lerp(heightNoiseHeight, config.coastTargetLandformHeight, coastAndForcedFactor), forcedPointWeight);
            }
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

        public float GetHeightMapAt(int xCoord, int zCoord) {
            return (float)(heightNoise.Height(xCoord, zCoord).Item1);
        }

        public void PrepareForNewHeightmap(int xCoord, int zCoord, int sizeX, int sizeZ) {
            heightMapValues = null;
            heightMapValues = new int[sizeX * sizeZ];
            mountainRangeMapValues = null;
            mountainRangeMapValues = new int[sizeX * sizeZ];
            upliftMapValues = null;
            upliftMapValues = new int[sizeX * sizeZ];
            heightMapRegionXSize = sizeX;
            heightMapRegionZSize = sizeZ;
            xPos = 0;
            zPos = 0;
        }

        public void SaveValueToHeightmap(double height, double mountainRangeOpacity, int upliftAmount) {
            int index = zPos * heightMapRegionXSize + xPos;
            heightMapValues[index] = (int)(height * significantDigitMult);
            mountainRangeMapValues[index] = (int)(GameMath.Clamp(mountainRangeOpacity, 0.0, 1.0) * 255);
            upliftMapValues[index] = upliftAmount;

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

        public IntDataMap2D GetMountainRangeData() {
            var pad = TerraGenConfig.landformMapPadding;
            var landformScale = sapi.WorldManager.RegionSize / TerraGenConfig.landformMapScale;
            int[] mountainRangeCopy = new int[mountainRangeMapValues.Length];
            mountainRangeMapValues.CopyTo(mountainRangeCopy, 0);

            return new IntDataMap2D {
                Data = mountainRangeCopy,
                Size = landformScale + 2 * pad,
                TopLeftPadding = pad,
                BottomRightPadding = pad
            };
        }

        public IntDataMap2D GetUpliftData() {
            var pad = TerraGenConfig.landformMapPadding;
            var landformScale = sapi.WorldManager.RegionSize / TerraGenConfig.landformMapScale;
            int[] upliftCopy = new int[upliftMapValues.Length];
            upliftMapValues.CopyTo(upliftCopy, 0);

            return new IntDataMap2D {
                Data = upliftCopy,
                Size = landformScale + 2 * pad,
                TopLeftPadding = pad,
                BottomRightPadding = pad
            };
        }
    }
}
