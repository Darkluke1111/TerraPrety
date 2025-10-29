using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.LandformHeights {

    public class MapLayerLandformsSmooth : MapLayerBase {

        public static LandformHeightNoise noiseLandforms;
        NoiseClimate climateNoise;

        NormalizedSimplexNoise noisegenX;
        NormalizedSimplexNoise noisegenY;
        float wobbleIntensity;
        bool forcedPointsInit;

        public float landFormHorizontalScale = 1f;

        public MapLayerLandformsSmooth(long seed, NoiseClimate climateNoise, ICoreServerAPI api, float landformScale, WorldGenConfig config) : base(seed) {
            this.climateNoise = climateNoise;
            forcedPointsInit = false;

            float scale = TerraGenConfig.landformMapScale * landformScale;

            scale *= Math.Max(1, (api.WorldManager.MapSizeY - 64) / 256f); //The -64 here is to account for the possible shifting of the world Downwards by 64 blocks, so we can fit more Mountain above.

            noiseLandforms = new LandformHeightNoise(seed, api, scale, config);

            int woctaves = 2;
            float wscale = 2f * TerraGenConfig.landformMapScale;
            float wpersistence = 0.9f;
            wobbleIntensity = TerraGenConfig.landformMapScale * 1.5f;
            noisegenX = NormalizedSimplexNoise.FromDefaultOctaves(woctaves, 1 / wscale, wpersistence, seed + 2);
            noisegenY = NormalizedSimplexNoise.FromDefaultOctaves(woctaves, 1 / wscale, wpersistence, seed + 1231296);
        }

        public override int[] GenLayer(int xCoord, int zCoord, int sizeX, int sizeZ) {
            if (!forcedPointsInit) {
                forcedPointsInit = true;
                noiseLandforms.SetForcedHeightPoints();
                noiseLandforms.FindForcedLandformID();
            }
            noiseLandforms.PrepareForNewHeightmap(xCoord, zCoord, sizeX, sizeZ);

            int[] result = new int[sizeX * sizeZ];
            for (int x = 0; x < sizeX; x++) {
                for (int z = 0; z < sizeZ; z++) {
                    int offsetX = (int)(wobbleIntensity * noisegenX.Noise(xCoord + x, zCoord + z) * 1.2f); //Respective Coord + the offset value from the loops is the 
                    int offsetY = (int)(wobbleIntensity * noisegenY.Noise(xCoord + x, zCoord + z) * 1.2f);

                    int finalX = xCoord + x + offsetX;
                    int finalZ = zCoord + z + offsetY;

                    int climate = climateNoise.GetLerpedClimateAt(finalX / TerraGenConfig.climateMapScale, finalZ / TerraGenConfig.climateMapScale);
                    int rain = climate >> 8 & 0xff;
                    int temp = Climate.GetScaledAdjustedTemperature(climate >> 16 & 0xff, 0);
                    
                    result[z * sizeX + x] = noiseLandforms.GetLandformIndexAt(
                        finalX,
                        finalZ,
                        temp,
                        rain
                    );
                }
            }

            return result;
        }

        //Send this the coords of a single tile of the LandformMap to recieve the heightmap value at that tile. Intended for use with far-reaching generation steps where this part of the map has not been generated yet, but the value is still needed. Use sparingly as this fully calculates it, if possible always just try accessing the saved region maps directly. (Used in River Gen currently)
        public float GetHeightMapAt(int xCoord, int zCoord) {
            if (!forcedPointsInit) {
                forcedPointsInit = true;
                noiseLandforms.SetForcedHeightPoints();
                noiseLandforms.FindForcedLandformID();
            }

            int offsetX = (int)(wobbleIntensity * noisegenX.Noise(xCoord, zCoord) * 1.2f); //Respective Coord + the offset value from the loops is the 
            int offsetY = (int)(wobbleIntensity * noisegenY.Noise(xCoord, zCoord) * 1.2f);

            int finalX = xCoord + offsetX;
            int finalZ = zCoord + offsetY;

            return noiseLandforms.GetHeightMapAt(finalX, finalZ);
        }

        public void AddForcedLandform(ForceLandform forced) {
            noiseLandforms.AddForcedLandform(forced);
        }

        public void AddHeightmapToRegion(IMapRegion region) {
            var heightMap = noiseLandforms.GetHeightData();
            region.ModMaps["LandformHeightMap"] = heightMap;
        }

        public void BorrowHeightMapReference(ref WeightedNormalizedSimplexNoise heightNoise, ref NormalizedSimplexNoise landformNoiseGenX, ref NormalizedSimplexNoise landformNoiseGenY) {
            noiseLandforms.BorrowHeightMapReference(ref heightNoise);
            landformNoiseGenX = noisegenX;
            landformNoiseGenY = noisegenY;
        }
    }
}
