using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace SmoothCoastlines.Rivers {

    public class River {

        public int ID;
        public List<RiverChunk> river;
        public Vec2d riverSource;
        public Vec2d riverSink;

    }
}
