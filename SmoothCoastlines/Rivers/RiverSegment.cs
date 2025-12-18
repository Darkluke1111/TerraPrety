using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.Rivers {

    public class RiverSegment { //An actual segment of the river. Contains a portion of the points that will produce the river. 
                                //Forks will be their own separate Segment that connects back to their confluence point in the segment that spawned them.

        public static int maxPointsPerSegment;

        public bool fullyGenerated = false; //Potentially rework this to account for RiverChunks that span over multiple chunks - will need to record each chunk's generated state potentially? To know exactly when this data can be cleared from RAM
        
        public RiverRegion riverRegion;
        public RiverSegment upstream; //These can be helpful to ensure each are interconnected.
        public RiverSegment downstream;
        public Dictionary<int, RiverSegment> forkSegments; //Any connecting segments that are a result of a fork. Key is the index of the point that it is connected to! Possibly helpful to parse through it?

        public List<RiverPoint> points; //This holds Chunk X and Z coordinates for the various chunks that this River Segment is going to generate in
        public RiverPoint upPoint => points.Last(); //Shortcuts for the upstream and downstream connecting points. Since it is built from Sink to Source, this should always stay the same.
        public RiverPoint downPoint => points.First(); //As above, Forks should spawn their own segment, which connects back to a confluence point in the spawning segment.

        public bool forkedSegment = false;
        public int forkedPointIndex;
        public ITreeAttribute segmentSpecialConditions; //If this segment contains a unique point, it should be recorded on this tree attribute. Potentially used for Waterfalls and other features? If this can be determined during the Map stage. Hmm...

        public float maxHeightMapOfSegment;
        public int minimumWorldX; //These should help to break apart the Segments into more managable pieces, and allow for a quicker way to see if this segment is important for any specific block
        public int minimumWorldZ;
        public int maximumWorldX;
        public int maximumWorldZ;

        public RiverSegment(RiverRegion region, RiverSegment downSegment = null) {
            riverRegion = region;
            downstream = downSegment;
            if (downstream != null) {
                downstream.AttachUpstream(this);
                if (downstream.forkedSegment) {
                    forkedSegment = true;
                }
            }

            points = new List<RiverPoint>();
            forkSegments = new Dictionary<int, RiverSegment>();
            forkedPointIndex = -1;
            segmentSpecialConditions = new TreeAttribute();
            minimumWorldX = -1;
            minimumWorldZ = -1;
            maximumWorldX = -1;
            maximumWorldZ = -1;
            
            maxPointsPerSegment = SmoothCoastlinesModSystem.config.maxPointsPerRiverSegment;
        }

        public void AttachUpstream(RiverSegment upSegment) {
            upstream = upSegment;
        }

        public bool PointValidForSegmentsRegion(XZ pointRegion) {
            if (riverRegion.regionCoords.X == pointRegion.X && riverRegion.regionCoords.Z == pointRegion.Z) {
                return true;
            } else {
                return false;
            }
        }

        //Returns a true if successful, false if not!
        public bool AddForkSegment(RiverSegment forkSegment, RiverPoint confluencePoint) {
            var index = points.IndexOf(confluencePoint);

            if (index >= 0) {
                if (forkSegments.TryAdd(index, forkSegment)) {
                    forkSegment.forkedSegment = true;
                    forkSegment.forkedPointIndex = index;
                    forkSegment.downstream = this;
                    return true;
                } else {
                    SmoothCoastlinesModSystem.Logger.Warning("Tried to add two forks on the same confluence point. This is not supported, canceling fork.");
                    return false;
                }
            } else {
                SmoothCoastlinesModSystem.Logger.Error("Attempted to add a Fork Segment to a point that does not exist in this segment.");
                return false;
            }
        }

        public void RemoveForkSegment(RiverPoint confluencePoint) {
            var index = points.IndexOf(confluencePoint);

            if (index >= 0) {
                forkSegments.Remove(index);
            }
        }

        //Returns a true or false for if this point was successfully added to this segment.
        //Be sure to check if it is a valid point for this segment first!
        public bool AddPointToSegment(RiverPoint point) {
            if (points.Count == 0) {
                minimumWorldX = point.worldX;
                minimumWorldZ = point.worldZ;
                maximumWorldX = point.worldX;
                maximumWorldZ = point.worldZ;
                points.Add(point);
                return true;
            }

            if (points.Count >= maxPointsPerSegment) {
                return false;
            }

            if (minimumWorldX > point.worldX) {
                minimumWorldX = point.worldX;
            }
            if (minimumWorldZ > point.worldZ) {
                minimumWorldZ = point.worldZ;
            }
            if (maximumWorldX < point.worldX) { //Is there ever a time where this might be true while the minimum comparison for X is also true? I don't believe so?
                maximumWorldX = point.worldX;
            }
            if (maximumWorldZ < point.worldZ) {
                maximumWorldZ = point.worldZ;
            }

            points.Add(point);
            return true;
        }

        public int NumPointsInSegment() {
            return points.Count;
        }

        public bool DoesSegmentEffectPoint(XZ worldCoords) {
            if (minimumWorldX <= worldCoords.X && maximumWorldX >= worldCoords.X) {
                if (minimumWorldZ <= worldCoords.Z && maximumWorldZ >= worldCoords.Z) {
                    return true;
                }
            }

            return false;
        }

        public void AverageOutFlows(float flowLoss) {
            float initFlow = downPoint.flowStrength;
            var numPoints = points.Count;
            var lossPerPoint = numPoints * flowLoss;

            for (int i = 1; i < numPoints; i++) {
                points[i].UpdateFlow((initFlow - (i * lossPerPoint)));
            }
        }
    }

    public class RiverPoint {
        public int worldX;
        public int worldY; //The actual y-height in blocks where this river point should "exist".
        public int worldZ;

        RiverPoint upstream = null; //These can go between segments! If one is null, it is the sink or a source depending on which!
        RiverPoint downstream = null;
        List<RiverPoint> confluenceConnections; //This is null for every point except for those that are a confluence point.

        public float flowStrength; //This helps to determine the overall depth and width of the chunk of the river.
        public float landformHMHeight;
        public int oceanicity;
        public bool pointAfterConfluence = false; //Set if AddConfluenceUpstream is called

        public RiverSegment parentSegment;

        public RiverPoint(int x, int y, int z, int oceanicity = 0, float str = 0, RiverSegment parent = null) { //Send this a -1 for Y if it is not set yet. The Y should be location of the top-most water source block, so anything above this is air.
            worldX = x;
            worldY = y;
            worldZ = z;
            this.oceanicity = oceanicity;
            flowStrength = str;
            confluenceConnections = null;
            parentSegment = parent;
        }

        public void UpdateParentSegment(RiverSegment seg) {
            parentSegment = seg;
        }

        public void UpdateFlow(float flow) {
            flowStrength = flow;
        }

        public void UpdateOceanicity(int ocean) {
            oceanicity = ocean;
        }

        public void AddUpstreamPoint(RiverPoint up) {
            upstream = up;
            up.downstream = this;
        }

        public void AddConfluenceUpstream(RiverPoint up) {
            confluenceConnections??= new List<RiverPoint>();
            confluenceConnections.Add(up);
            up.downstream = this;
            up.pointAfterConfluence = true;
        }

        public void CancelConfluence(RiverPoint up) {
            confluenceConnections?.Remove(up);
            up.pointAfterConfluence = false;
        }

        public XZ GetDirectionTo(RiverPoint target) {
            XZ direction = new XZ();
            var xFac = target.worldX - worldX;
            var zFac = target.worldZ - worldZ;

            if (xFac > 0) {
                direction.X = 1;
            } else if (xFac < 0) {
                direction.X = -1;
            } else {
                direction.X = 0;
            }

            if (zFac > 0) {
                direction.Z = 1;
            } else if (xFac < 0) {
                direction.Z = -1;
            } else {
                direction.Z = 0;
            }

            return direction;
        }

        public bool HasUpstream() {
            return upstream != null;
        }

        public bool HasDownstream() {
            return downstream != null;
        }

        public RiverPoint GetUpstream() {
            return upstream;
        }

        public RiverPoint GetDownstream() {
            return downstream;
        }
    }
}
