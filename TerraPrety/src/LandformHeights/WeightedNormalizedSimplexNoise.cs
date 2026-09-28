using System;
using System.Collections.Generic;
using TerraPrety.Noise;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace TerraPrety.LandformHeights {
    public class WeightedNormalizedSimplexNoise { 

        private NormalizedSimplexNoise SimplexNoise;
        private MidpointDistFromPointNoise MidZonePointGen; //This is used to generate blobs of 'mid height' terrain randomly around the world.
        private NoiseRemapper RemappedMidZone;
        private NoiseRemapper InlandMountainRangeMask;
        private float MountainTargetHeight;
        private List<RequiredHeightPoints> RequiredPoints; //Any point here has a specific height for the Landform it is expecting, and this holds that min-max height.
        private float PointsOutwardsNeedingAverage; //This many steps outwards from a required point will be adjusted towards the required height.
        private float MidTargetHeight;
        private float LowThreshForNoMids;
        private float LowThreshSmoothFactor = 0.1f;

        public WeightedNormalizedSimplexNoise(int quantityOctaves, double baseFrequency, double persistance, long seed, float pointsOutForAverage, double landformScale, float chanceForMidZone, double[] midKeys, double[] midValues, float midTargetHeight, float midLowThresh, float inlandMountainRangeScaleMult, double[] inlandMountainRangeKeys, double[] inlandMountainRangeValues, float mountainRangesPullsHeightMapTowards) {
            SimplexNoise = NormalizedSimplexNoise.FromDefaultOctaves(quantityOctaves, baseFrequency, persistance, seed);
            PointsOutwardsNeedingAverage = pointsOutForAverage;

            MidZonePointGen = new MidpointDistFromPointNoise(seed, landformScale, chanceForMidZone);
            RemappedMidZone = new NoiseRemapper(MidZonePointGen, midKeys, midValues);
            MidTargetHeight = midTargetHeight;
            LowThreshForNoMids = midLowThresh;

            var inlandMountainRangeVoronoi = new VoronoiNoise(seed + 7919, landformScale * inlandMountainRangeScaleMult, []);
            InlandMountainRangeMask = new NoiseRemapper(new VoronoiRidgeNoise(inlandMountainRangeVoronoi), inlandMountainRangeKeys, inlandMountainRangeValues);
            MountainTargetHeight = mountainRangesPullsHeightMapTowards;
        }

        public void SetRequiredPoints(List<RequiredHeightPoints> reqPoints) {
            RequiredPoints = reqPoints;
        }

        public double InlandMountainRangeMaskValue(int x, int z) => InlandMountainRangeMask.getValueAt(x, z);

        // Lift the landform height towards the mountain target height within mountain ranges
        public double LiftTowardMountainRangeTargetHeight(double unliftedHeight, double mountainRangeOpacity)
        {
            if (mountainRangeOpacity <= 0.0)
                return unliftedHeight;

            double liftedHeight = GameMath.Lerp(unliftedHeight, MountainTargetHeight, mountainRangeOpacity);
            if (liftedHeight > unliftedHeight)
                return (double)liftedHeight;
            else // No down
                return (double)unliftedHeight;
        }

        public bool IsInForcedLandform(int x, int z)
        {
            if (RequiredPoints == null || RequiredPoints.Count == 0)
                return false;

            foreach (RequiredHeightPoints point in RequiredPoints)
            {
                if (point.x == x && point.z == z)
                    return true;

                int scaledRadius = (int)(PointsOutwardsNeedingAverage * point.radius);
                scaledRadius += scaledRadius / 2;
                if (point.IsWithinRange(x, z, scaledRadius))
                    return true;
            }

            return false;
        }

        public (double, double) Height(int x, int z) {
            RequiredHeightPoints foundPoint = new RequiredHeightPoints(0,0,100,0,1); //This should never be accessed unless it's actually properly replaced.
            bool wasWithinRange = false;
            int scaledRadius;
            if (RequiredPoints != null && RequiredPoints.Count > 0) {
                int lowestDistance = int.MaxValue;
                foreach (var p in RequiredPoints) {
                    if (p.x == x && p.z == z) { //If the polled point actually is the weighted point, just return the center height and we are good to go.
                        return (p.centerHeight, 1.0);
                    }
                    scaledRadius = (int)(PointsOutwardsNeedingAverage * p.radius);
                    scaledRadius += scaledRadius / 2;
                    if (p.IsWithinRange(x, z, scaledRadius)) {
                        wasWithinRange = true;
                        var dist = p.DistanceTo(x, z);
                        if (dist < lowestDistance) {
                            lowestDistance = dist;
                            foundPoint = p;
                        }
                    }
                }
            }

            var height = SimplexNoise.Noise(x, z); //First grab the height.
            var adjustedHeight = height;

            var lowerBound = LowThreshForNoMids - LowThreshSmoothFactor;
            if (adjustedHeight >= lowerBound) {
                var withinMidDist = MidZonePointGen.getValueAt(x, z); //Add in Mid-Points that will blend the terrain towards a mid level within these areas. Kinda borrowing the Voronoi setup but only looking at a singular point.
                if (withinMidDist >= 0.0) {
                    withinMidDist = RemappedMidZone.getValueAt(x, z);

                    var upperBound = LowThreshForNoMids + LowThreshSmoothFactor;
                    if (adjustedHeight <= upperBound) {
                        var distInSmoothLerpZone = (adjustedHeight - lowerBound) / (upperBound - lowerBound);
                        var oldDistFromMid = withinMidDist;
                        withinMidDist = GameMath.Lerp(0, oldDistFromMid, distInSmoothLerpZone);
                    }

                    adjustedHeight = GameMath.Lerp(height, MidTargetHeight, withinMidDist);
                }
            }

            if (wasWithinRange) { //If the point was within the range of the required heights...
                //Handle the smoothing here. foundPoint is set.
                scaledRadius = (int)(PointsOutwardsNeedingAverage * foundPoint.radius);
                var centerHeightWeight = GetAdjustmentFromGaussian(scaledRadius, foundPoint, x, z); //This SHOULD return a double from 0 - 1, which is how strong of a 'pull' should the center point have over the current height
                adjustedHeight = GameMath.Lerp(adjustedHeight, foundPoint.centerHeight, centerHeightWeight);

                return (adjustedHeight, centerHeightWeight);
            }

            return (adjustedHeight, 0.0);
        }

        public double GetAdjustmentFromGaussian(int radius, RequiredHeightPoints foundPoint, int x, int z) {
            float radiusSquare = MathF.Pow(radius/2, 2); //Gaussian Function to find the percentage to adjust the height by!
            var dx = foundPoint.x - x;
            var dz = foundPoint.z - z;

            return Math.Exp(-((Math.Pow(dx, 2) / (2 * radiusSquare)) + (Math.Pow(dz, 2) / (2 * radiusSquare))));
        }
    }
}
