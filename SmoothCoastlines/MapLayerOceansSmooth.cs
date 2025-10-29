
using SmoothCoastlines;
using SmoothCoastlines.LandformHeights;
using SmoothCoastLines.Noise;
using System;
using System.Collections.Generic;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace MapLayer
{
    class MapLayerOceansSmooth : MapLayerBase
    {
        NormalizedSimplexNoise noisegenX;
        NormalizedSimplexNoise noisegenY;
        float wobbleIntensity;
        private WorldGenConfig config;
        VoronoiNoise voronoiNoise;
        Noise2D oceanNoise;

        public float landFormHorizontalScale = 1f;

        public MapLayerOceansSmooth(long seed, WorldGenConfig config, List<XZ> requireLandAt) : base(seed)
        {
            this.config = config;

            voronoiNoise = new VoronoiNoise(seed + 2, config.noiseScale, requireLandAt);
            oceanNoise = new NoiseRemapper(voronoiNoise, config.remappingKeys, config.remappingValues);

            int woctaves = 4;
            float wscale = config.oceanWobbleScale * config.noiseScale;
            float wpersistence = 0.9f;
            wobbleIntensity = config.oceanWobbleIntensity * config.noiseScale;
            noisegenX = NormalizedSimplexNoise.FromDefaultOctaves(woctaves, 1 / wscale, wpersistence, seed + 2);
            noisegenY = NormalizedSimplexNoise.FromDefaultOctaves(woctaves, 1 / wscale, wpersistence, seed + 1231296);
        }

        public override int[] GenLayer(int xCoord, int zCoord, int sizeX, int sizeZ)
        {
            var result = new int[sizeX * sizeZ];
            for (var x = 0; x < sizeX; x++)
            {
                for (var z = 0; z < sizeZ; z++)
                {
                    var nx = xCoord + x;
                    var nz = zCoord + z;
                    var undestortedNoise = voronoiNoise.getValueAt(nx, nz); ;
                    var offsetX = (int)(wobbleIntensity * noisegenX.Noise(nx, nz) * undestortedNoise);
                    var offsetZ = (int)(wobbleIntensity * noisegenY.Noise(nx, nz) * undestortedNoise);
                    var unscaledXpos = nx + offsetX;
                    var unscaledZpos = nz + offsetZ;
                    var oceanicity = oceanNoise.getValueAt(unscaledXpos, unscaledZpos);

                    result[z * sizeX + x] = (int)(oceanicity * 255);
                }
            }

            return result;
        }

        //Send this the coords of a single tile of the OceanMap to recieve the Oceanicity for that tile. Intended for use with far-reaching generation steps where this part of the map has not been generated yet, but the value is still needed. Use sparingly as this fully calculates it, if possible always just try accessing the saved region maps directly. (Used in River Gen currently)
        public int GetOceanicityAt(int xCoord, int zCoord) {
            var undestortedNoise = voronoiNoise.getValueAt(xCoord, zCoord); ;
            var offsetX = (int)(wobbleIntensity * noisegenX.Noise(xCoord, zCoord) * undestortedNoise);
            var offsetZ = (int)(wobbleIntensity * noisegenY.Noise(xCoord, zCoord) * undestortedNoise);
            var unscaledXpos = xCoord + offsetX;
            var unscaledZpos = zCoord + offsetZ;

            return (int)(oceanNoise.getValueAt(unscaledXpos, unscaledZpos) * 255);
        }

        public XZ GetContinentalCenter(int xCoord, int zCoord) {
            var undestortedNoise = voronoiNoise.getValueAt(xCoord, zCoord); ;
            var offsetX = (int)(wobbleIntensity * noisegenX.Noise(xCoord, zCoord) * undestortedNoise);
            var offsetZ = (int)(wobbleIntensity * noisegenY.Noise(xCoord, zCoord) * undestortedNoise);
            var unscaledXpos = xCoord + offsetX;
            var unscaledZpos = zCoord + offsetZ;

            return voronoiNoise.GetContinentalCenter(unscaledXpos, unscaledZpos);
        }
    }
}
