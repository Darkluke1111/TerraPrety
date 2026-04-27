using SmoothCoastLines.Noise;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.LandformHeights {

    public class MidpointDistFromPointNoise : NoiseBase, Noise2D {

        double scale;
        float chanceForMidZone;
        const double maxDistanceConstant = 1.41421356237309505; //Square Root of 2

        public MidpointDistFromPointNoise(long seed, double scale, float chanceForMid) : base(seed) {
            this.scale = scale;
            this.chanceForMidZone = chanceForMid;
        }

        public double getValueAt(int unscaledXpos, int unscaledZpos) {
            double xpos_full = unscaledXpos / scale;
            double zpos_full = unscaledZpos / scale;

            //Integer part of the position is the center point coordinate
            int xCell = (int)xpos_full;
            int zCell = (int)zpos_full;
            InitPositionSeed(xCell, zCell);
            if (chanceForMidZone < (NextInt(10000) / 10000.0)) {
                return -1.0;
            }

            //Fractional part is the location relative to the center point
            double xFrac = xpos_full - xCell;
            double zFrac = zpos_full - zCell;

            double distance = GameMath.Sqrt((xFrac - 0.5) * (xFrac - 0.5) + (zFrac - 0.5) * (zFrac - 0.5));

            // Normalize to [0,1] and return
            return distance / maxDistanceConstant;
        }
    }
}
