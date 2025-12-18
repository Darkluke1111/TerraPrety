namespace SmoothCoastlines
{
    public class WorldGenConfig
    {
        public float noiseScale = 300.0f;
        public float heightMapNoiseScale = 32.0f;
        public string fallbackParentLandformCode = "ultraflats"; //This is just in case it somehow rolls a height value with no valid Landforms that would fit it, it will use this one instead.

        public bool Delicate_configs_below__alter_at_your_own_peril = false;

        public float oceanWobbleScale = 2.0f;
        public float oceanWobbleIntensity = 2.0f;
        public double[] remappingKeys = { 0.165, 0.335 };
        public double[] remappingValues = {0.0, 1.0 };

        public int heightMapOctaves = 1;
        public float heightMapPersistance = 0.1f;

        public double[] midHeightKeys = { 0.0, 0.05, 0.33333, 1.0 };
        public double[] midHeightValues = { 1.0, 0.9, 0.0, 0.0 };
        public float chanceForMidZone = 0.8f;
        public float targetMidLevel = 0.3f;
        public float lowThreshForMidZone = 0.25f;

        public float radiusMultOutwardsForSmoothing = 6.0f;

        public int hardMinimumCoastalOceanicity = 1;
        public int softMinimumCoastalOceanicity = 30;
        public int maximumCoastalOceanicity = 256;

        // -- Rivers related settings follow! --

        public float chanceForRiver = 0.2f; //The chance for each valid region found, should it contain the start of a river?
        public int minimumRiverOceanicity = 5;
        public int maximumRiverOceanicity = 100;
        public float maxHeightForRiverSink = 0.25f; //Based on the LandformHeightMap heights, not actual y-heights.
        public float chanceToFork = 0.02f;

        public float minimumRiverFlowStrength = 0.25f;
        public float maximumRiverFlowStrength = 1.5f;

        public int maxPointsPerRiverSegment = 10; //Aim to generate a full segment's worth of points before adding them all to the segment and then to the region.
        public float primaryRiverHeightStepFlex = 0.025f; //A primary river step must have a LandformHeightMap value that is only this distance from the current to be considered valid.
        public float tributaryRiverHeightStepFlex = 0.02f; //Mainly just the downward flexibility, since upwards is not really capped for Tributaries, just a desired target for a step.
        public float tributaryDesiredHeightStepUp = 0.04f; //Try to aim for a step that would be at least this amount higher then the current HeightMap value.
        public int riverOceanicityStepFlexibility = 5; //SLIGHT amount of leeway to allow the river to still somewhat travel to the sides, but trend inland.

        public float flowLossPerRiverSegment = 0.03f; //This serves as a hard-stop for a River to cease expanding if the flow gets below 0. Lower Value means longer rivers, generally, unless something else stops it first.

        /*public double terrainNoiseFrequencyMult = 1.0;
        public double terrainNoisePersistance = 0.9;
        public bool enableEdgeLandformSmoothing = false;
        public int landformSmoothingRadius = 3;
        public int landformMapPadding = 4;*/
    }
}