using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.Rivers {

    public class RiverRegion {

        public bool fullyGenerated = false;
        public float upstreamFlowStrength = -1f;
        public float downstreamFlowStrength; //For either of these, if it is negative, it means the river ends in this region. Recorded here to ensure that each chunk relatively syncs up with one another, and it gradually tapers down as it gets to the source.
        public float averageHeight; //This region's average Heightmap Height. A very rough estimate for simplicity sake.
        public RiverData river;
        public Dictionary<XZ, RiverChunk> chunks;
        public RiverRegion upstreamRegion; //Either of these can be null if there is nothing more in that direction.
        public RiverRegion downstreamRegion;
        public RiverRegion secondaryUpstreamRegion; //These are potential connection points for possible forks in the river.
        public RiverRegion tertiaryUpstreamRegion; // <-- Not guaranteed to be set at all, instantiated to null in the constructor.
        public XZ regionCoords; //The X and Z in Region Coordinates for this RiverRegion
        public XZ upstreamWorldPos = new XZ(-1, -1); //The X and Z (in actual World Coordinates) of the center of the river where it touches the edge of this Region. Should match up with the respective region's counterpart value.
        public XZ downstreamWorldPos = new XZ(-1, -1); //If either point has negative coordinates, that means there is nothing in that direction. The river either starts or ends in this Region.
        public XZ secondaryUpstreamWorldPos = new XZ(-1, -1);
        public XZ tertiaryUpstreamWorldPos = new XZ(-1, -1);
        public List<string> regionSpecialConditions; //Tags to add a flag here for use later during generation steps.

        public RiverRegion(RiverData river, XZ regionCoords, float avgHeight, float downstreamFlow, RiverRegion downRegion = null) { //A fork will send a Null downRegion here and handle setting the downRegion in the 'attachForkRegion' call below
            this.river = river;
            chunks = new Dictionary<XZ, RiverChunk>();
            regionSpecialConditions = new List<string>();
            this.regionCoords = regionCoords;
            averageHeight = avgHeight;
            downstreamFlowStrength = downstreamFlow;
            downstreamRegion = downRegion;
            if (downstreamRegion != null) {
                downstreamRegion.upstreamRegion = this;
                downstreamRegion.upstreamFlowStrength = downstreamFlow;
                downstreamWorldPos = downstreamRegion.upstreamWorldPos;
            }

            upstreamRegion = null;
            secondaryUpstreamRegion = null;
            tertiaryUpstreamRegion = null;
        }

        //To be called on the Downstream Region, and provided the Forked Region Upstream from it.
        public void AttachForkRegion(RiverRegion upstream, XZ upstreamPos) {
            if (secondaryUpstreamRegion == null) {
                secondaryUpstreamRegion = upstream;
                secondaryUpstreamWorldPos = upstreamPos;
                upstream.downstreamRegion = this;
                upstream.downstreamWorldPos = upstreamPos;
            } else if (tertiaryUpstreamRegion == null) {
                tertiaryUpstreamRegion = upstream;
                tertiaryUpstreamWorldPos = upstreamPos;
                upstream.downstreamRegion = this;
                upstream.downstreamWorldPos = upstreamPos;
            }
        }

        public void AddSpecialRegionTag(string tag) {
            regionSpecialConditions.Add(tag);
        }

        //This will hold either a -1, 1, or 0 in both X or Z to represent the step in the Region Coords to get to the desired region. -Z = N, +Z = S, -X = W, +X = E
        //Calculate by subtracting the desired direction's Region Coords by the current Region Coords.
        public XZ GetDirectionTo(XZ desiredRegion) {
            XZ direction = new XZ();
            direction.X = desiredRegion.X - regionCoords.X;
            direction.Z = desiredRegion.Z - regionCoords.Z;
            return direction;
        }
    }

    public struct RegionStepDirectionChances {
        public bool NorthFree = true;
        public bool WestFree = true;
        public bool EastFree = true;
        public bool SouthFree = true;

        public float NorthHeight = 0;
        public float WestHeight = 0;
        public float EastHeight = 0;
        public float SouthHeight = 0;

        public int NorthChance;
        public int WestChance;
        public int EastChance;
        public int SouthChance;

        public RegionStepDirectionChances(int baseChance) {
            NorthChance = baseChance;
            WestChance = baseChance;
            EastChance = baseChance;
            SouthChance = baseChance;
        }

        public void PreventDirectionChoice(XZ dir) {
            if (dir.X != 0) {
                if (dir.X < 0) {
                    WestFree = false;
                    WestChance = 0;
                } else {
                    EastFree = false;
                    EastChance = 0;
                }
            } else {
                if (dir.Z < 0) {
                    NorthFree = false;
                    NorthChance = 0;
                } else {
                    SouthFree = false;
                    SouthChance = 0;
                }
            }
        }

        public int GetFullChance() {
            return NorthChance + WestChance + EastChance + SouthChance;
        }
    }
}
