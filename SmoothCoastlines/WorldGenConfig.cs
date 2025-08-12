using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace SmoothCoastlines
{
    public class WorldGenConfig
    {
        public float noiseScale = 256.0f;
        public float heightMapNoiseScale = 32.0f;
        public string fallbackParentLandformCode = "ultraflats"; //This is just in case it somehow rolls a height value with no valid Landforms that would fit it, it will use this one instead.

        public bool Delicate_configs_below__alter_at_your_own_peril = false;

        public float oceanWobbleScale = 1.5f;
        public float oceanWobbleIntensity = 0.75f;
        public double[] remappingKeys = { 0.125, 0.45 };
        public double[] remappingValues = {0.0, 1.0 };

        public int heightMapOctaves = 1;
        public float heightMapPersistance = 0.1f;

        public double[] midHeightKeys = { 0.0, 0.05, 0.33333, 1.0 };
        public double[] midHeightValues = { 1.0, 0.9, 0.0, 0.0 };
        public float chanceForMidZone = 0.8f;
        public float targetMidLevel = 0.5f;
        public float lowThreshForMidZone = 0.35f;

        public float radiusMultOutwardsForSmoothing = 6.0f;

        public float[] heightThresholdsForOceanicityComp = { 0.2f, 0.5f, 0.7f, 0.8f, 1.0f };
        public float[] heightMultsAtThresholdsForOceanicityComp = { 0.0f, 70.0f, 70.0f, 115.0f, 115.0f };
        public float[] heightFlatsAtThresholdsForOceanicityComp = { 4.2f, 2f, 2f, 4f, 4f };
        //public float heightAboveWhichToWatchOceanicity = 0.8f;
        //public float highHeightLowOceanicityMin = 6.15f;
        //public float highHeightLowOceanicityMax = 24.6f;
        //public float heightMidAboveWhichToWatchOceanicity = 0.5f;
        //public float midHeightMidOceanicityMin = 4.1f;
        //public float midHeightMidOceanicityMax = 16.4f; //These values are the oceanicity at the spot multiplied by the OceanicityFactor, this is what it recieves so it makes it easier to calculate them

        public double terrainNoiseFrequencyMult = 1.0;
        public double terrainNoisePersistance = 0.9;
        public bool enableEdgeLandformSmoothing = false;
        public int landformSmoothingRadius = 3;
        public int landformMapPadding = 4;
    }
}