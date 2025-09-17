using Cairo;
using SmoothCoastlines;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

namespace SmoothCoastLines.Noise {

    // Basic voronoi noise with the ability to force the position of some voronoi points based on a list.
    interface Noise2D {
        double getValueAt(int unscaledXpos, int unscaledZpos);
    }

    public class VoronoiNoise: NoiseBase, Noise2D {

        double scale;
        const double maxDistanceConstant = 1.41421356237309505; //Square Root of 2
        List<XZ> forcedPoints;
        
        public VoronoiNoise(long seed, double scale, List<XZ> forcedPoints) : base(seed) {
            this.scale = scale;
            this.forcedPoints = forcedPoints;
        }

        //Will generate the voronoi noise value at the given point normalized to [0,1]
        public double getValueAt(int unscaledXpos, int unscaledZpos) {
            double xpos_full = unscaledXpos / scale;
            double zpos_full = unscaledZpos / scale;

            //Integer part of the position is the voronoi square coordinate
            int xCell = (int)xpos_full;
            int zCell = (int)zpos_full;

            //Fractional part is the location relative to the voronoi square
            double xFrac = xpos_full - xCell;
            double zFrac = zpos_full - zCell;
            XZ cellXZ = new XZ(xCell, zCell);

            double min_distance = Double.MaxValue;

            // Iterate over the voronoi square and its 8 nighbours
            for (int dx = 0; dx < 3; dx++) {
                for (int dz = 0; dz < 3; dz++) {
                    double pointPosX;
                    double pointPosZ;

                    //First check whether we have forced voronoi points in this cell
                    bool forced = false;
                    for(int i = 0; i < forcedPoints.Count; i++) {
                        double forcedX = forcedPoints[i].X / scale;
                        double forcedY = forcedPoints[i].Z / scale;
                        if (xCell - 1 + dx < forcedX && xCell - 1 + dx + 1 >= forcedX
                            && zCell - 1 + dz < forcedY && zCell - 1 + dz + 1 >= forcedY)
                        {
                            pointPosX = forcedX - xCell;
                            pointPosZ = forcedY - zCell;
                            forced = true;

                            var distance = GameMath.Sqrt((xFrac - pointPosX) * (xFrac - pointPosX) + (zFrac - pointPosZ) * (zFrac - pointPosZ));
                            if (min_distance > distance)
                            {
                                min_distance = distance;
                            }
                        }
                    }
                    // Generate a random voronoi point for the cell if none is forced
                    if(!forced)
                    {
                        InitPositionSeed(xCell - 1 + dx, zCell - 1 + dz);
                        pointPosX = (NextInt(10000) / 10000.0) - 1 + dx;
                        pointPosZ = (NextInt(10000) / 10000.0) - 1 + dz;

                        var distance = GameMath.Sqrt((xFrac - pointPosX) * (xFrac - pointPosX) + (zFrac - pointPosZ) * (zFrac - pointPosZ));
                        if (min_distance > distance)
                        {
                            min_distance = distance;
                        }
                    }
                }
            }

            // Normalize to [0,1] and return
            return min_distance / maxDistanceConstant;
        }
    }
}
