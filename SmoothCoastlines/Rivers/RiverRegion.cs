using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.Rivers {

    public class RiverRegion { //This now is more of a handler for this region's segments, providing ways to organize and compare the segments, which in turn handle the points themselves.

        public bool fullyGenerated = false;

        public XZ regionCoords; //The X and Z in Region Coordinates for this RiverRegion. This contains and controls the portion of this River that flows through this Region in particular.
        public RiverData river;
        public List<RiverRegion> connectedRegions; //Not really guaranteed to be in any order, except for the Primary River's downstream region will be added on first, then the forks will run, those nearest the sink will go first.

        public List<RiverSegment> primarySegments; //This is mainly just to store all of the segments in this Region. The order MIGHT be from sink to source like it was generated, but if a River loops through this same region again, it will likely throw things off some!
        public List<RiverSegment> forkedSegments; //A list of all segments that are a result of a fork in this region - relies on the Segments themselves being interconnected for continuity. This simply stores them all in one container.

        public int numberOfPoints;

        public List<string> regionSpecialConditions; //Tags to add a flag here for use later during generation steps.

        //These are probably not needed anymore, either.
        /*public XZ upstreamWorldPos = new XZ(-1, -1); //The X and Z (in actual World Coordinates) of the center of the river where it touches the edge of this Region. Should match up with the respective region's counterpart value.
        public XZ downstreamWorldPos = new XZ(-1, -1); //If either point has negative coordinates, that means there is nothing in that direction. The river either starts or ends in this Region.
        public XZ secondaryUpstreamWorldPos = new XZ(-1, -1);
        public XZ tertiaryUpstreamWorldPos = new XZ(-1, -1);

        public float upstreamFlowStrength = -1f;
        public float downstreamFlowStrength; //For either of these, if it is negative, it means the river ends in this region. Recorded here to ensure that each chunk relatively syncs up with one another, and it gradually tapers down as it gets to the source.
        public Dictionary<XZ, RiverSegment> chunks;
        public float averageHeight; //This region's average Heightmap Height. A very rough estimate for simplicity sake.

        // Consider if these 4 connections below are even needed anymore. Since moving to the more point-based markers, everything is more down on the Segment level?
        public RiverRegion upstreamRegion; //Either of these can be null if there is nothing more in that direction.
        public RiverRegion downstreamRegion;
        public RiverRegion secondaryUpstreamRegion; //These are potential connection points for possible forks in the river. Wait, is this not needed really anymore? These don't really _need_ to be connected like this, the Segments handle that, and better.
        public RiverRegion tertiaryUpstreamRegion; // <-- Not guaranteed to be set at all, instantiated to null in the constructor.
        */
        public RiverRegion(RiverData river, XZ regionCoords) { //A fork will send a Null downRegion here and handle setting the downRegion in the 'attachForkRegion' call below
            this.regionCoords = regionCoords;
            this.river = river;
            connectedRegions = new List<RiverRegion>();
            primarySegments = new List<RiverSegment>();
            forkedSegments = new List<RiverSegment>();
            regionSpecialConditions = new List<string>();

            numberOfPoints = 0;

            /* -- float avgHeight, float downstreamFlow, RiverRegion downRegion = null
            chunks = new Dictionary<XZ, RiverSegment>();
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
            tertiaryUpstreamRegion = null;*/
        }

        public void ConnectRegion(RiverRegion connectedRegion) {
            connectedRegions.Add(connectedRegion);
            connectedRegion.connectedRegions.Add(this);
        }

        public void AddPrimarySegment(RiverSegment primary) {
            primarySegments.Add(primary);
        }

        public void AddForkSegment(RiverSegment forkSegment) {
            forkedSegments.Add(forkSegment);

            /*var index = primarySegments.IndexOf(originSegment);

            if (index >= 0) {
                if (!forks.TryAdd(index, forkSegments)) {
                    var forkAtOrigin = forks[index];
                    forkAtOrigin.AddRange(forkSegments);
                }
            } else {
                SmoothCoastlinesModSystem.Logger.Error("Attempted to add a forked segment to a region that does not contain this origin segment. Something went wrong.");
            }*/

            /*if (secondaryUpstreamRegion == null) {
                secondaryUpstreamRegion = upstream;
                secondaryUpstreamWorldPos = upstreamPos;
                upstream.downstreamRegion = this;
                upstream.downstreamWorldPos = upstreamPos;
            } else if (tertiaryUpstreamRegion == null) {
                tertiaryUpstreamRegion = upstream;
                tertiaryUpstreamWorldPos = upstreamPos;
                upstream.downstreamRegion = this;
                upstream.downstreamWorldPos = upstreamPos;
            }*/
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

        public bool DoesRiverPassThroughMapTile(XZ worldCoords) {
            foreach (RiverSegment seg in primarySegments) {
                if (seg.DoesSegmentEffectPoint(worldCoords)) {
                    return true;
                }
            }

            foreach (RiverSegment seg in forkedSegments) {
                if (seg.DoesSegmentEffectPoint(worldCoords)) {
                    return true;
                }
            }

            return false;
        }
    }
}
