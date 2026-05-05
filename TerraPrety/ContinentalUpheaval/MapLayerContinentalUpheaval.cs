using System;
using System.Collections.Generic;
using TerraPrety.Noise;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace TerraPrety.ContinentalUpheaval {

    public class MapLayerContinentalUpheaval : MapLayerBase {

        NormalizedSimplexNoise noisegenX;
        NormalizedSimplexNoise noisegenY;
        float wobbleIntensity;
        private WorldGenConfig config;
        float upliftAmount;
        VoronoiNoise voronoiNoise;
        Noise2D conUpheavalNoise;

        public MapLayerContinentalUpheaval (long seed, WorldGenConfig config, List<XZ> requireLandAt) : base(seed) {
            this.config = config;

            voronoiNoise = new VoronoiNoise(seed + 2, config.noiseScale, requireLandAt); //Want this to be the same as the SmoothOcean Map! So that it can fit and return the same values, just for uplifting continents instead of sinking down the whole world.
            conUpheavalNoise = new NoiseRemapper(voronoiNoise, new double[] { 0.0 }, new double[] { 0.0 }); //config.continentalUpheavalKeys config.continentalUpheavalValues

            upliftAmount = 0;//config.continentalUpheavalUplift;
            int octaves = 4;
            float scale = config.oceanWobbleScale * config.noiseScale;
            float persistence = 0.9f;
            wobbleIntensity = config.oceanWobbleIntensity * config.noiseScale;
            noisegenX = NormalizedSimplexNoise.FromDefaultOctaves(octaves, 1 / scale, persistence, seed + 2);
            noisegenY = NormalizedSimplexNoise.FromDefaultOctaves(octaves, 1 / scale, persistence, seed + 1231296);
        }

        public override int[] GenLayer(int xCoord, int zCoord, int sizeX, int sizeZ) {
            var result = new int[sizeX * sizeZ];
            for (var x = 0; x < sizeX; x++) {
                for (var z = 0; z < sizeZ; z++) {
                    var nx = xCoord + x;
                    var nz = zCoord + z;
                    var undestortedNoise = voronoiNoise.getValueAt(nx, nz); ;
                    var offsetX = (int)(wobbleIntensity * noisegenX.Noise(nx, nz) * undestortedNoise);
                    var offsetZ = (int)(wobbleIntensity * noisegenY.Noise(nx, nz) * undestortedNoise);
                    var unscaledXpos = nx + offsetX;
                    var unscaledZpos = nz + offsetZ;
                    var conUpheaval = conUpheavalNoise.getValueAt(unscaledXpos, unscaledZpos);

                    result[z * sizeX + x] = -(int)Math.Round(conUpheaval * upliftAmount);
                }
            }

            return result;
        }
    }
}
