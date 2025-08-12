using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.Rivers {

    public class RiverMap : MapLayerBase {

        public RiverMap(long seed) : base(seed) {

        }

        public override int[] GenLayer(int xCoord, int zCoord, int sizeX, int sizeZ) {
            throw new NotImplementedException();
        }
    }
}
