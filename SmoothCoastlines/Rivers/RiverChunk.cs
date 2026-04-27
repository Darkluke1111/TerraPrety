using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace SmoothCoastlines.Rivers {

    public class RiverChunk {

        public int ID;
        public float waterLevel;
        public River parent;
        public Vec2d upstreamPos;
        public Vec2d downstreamPos;
        public RiverChunk upstream;
        public RiverChunk downstream;
        public bool specialChunk = false;

        public RiverChunk(River parent, int id, float water, Vec2d upPos, Vec2d downPos, RiverChunk downChunk = null) {
            this.parent = parent;
            ID = id;
            waterLevel = water;
            upstreamPos = upPos;
            downstreamPos = downPos;
            if (downChunk != null) {
                downstream = downChunk;
            }
        }

        public void AttachUpstream(RiverChunk upChunk) {
            upstream = upChunk;
        }
    }
}
