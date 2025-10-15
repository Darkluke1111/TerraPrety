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

    public class RiverChunk { //An actual segment of the river. Named 'Chunk' due to being on the Chunk scale, but more accurately it is a segment of the river. Extend this to add variety and other special shapes to the river generation.

        public bool fullyGenerated = false; //Potentially rework this to account for RiverChunks that span over multiple chunks - will need to record each chunk's generated state potentially? To know exactly when this data can be cleared from RAM
        public float flowStrength; //This helps to determine the overall depth and width of the chunk of the river.
        public RiverRegion riverRegion;
        public RiverChunk upstream;
        public RiverChunk downstream;
        public List<XZ> occupiedChunks; //This holds Chunk X and Z coordinates for the various chunks that this River Segment is going to generate in
        public XYZ upstreamWorldPos;
        public XYZ downstreamWorldPos;
        public TreeAttribute chunkSpecialConditions;

        public RiverChunk(RiverRegion region, float regionFlow, XYZ upPos, XYZ downPos, RiverChunk downChunk = null) {
            riverRegion = region;
            flowStrength = regionFlow;
            upstreamWorldPos = upPos;
            downstreamWorldPos = downPos;
            downstream = downChunk;
        }

        public void AttachUpstream(RiverChunk upChunk) {
            upstream = upChunk;
        }
    }
}
