using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.Rivers {

    //This represents a singular River. The WHOLE of the river, of which each part can be in various states of completeness.
    //To ease up on the RAM usage, any part that is connected on both ends to completely generated parts can be cleared and replaced with a placeholder chunk - to keep the List in order.
    //This is true for the full river itself as well. If it's fully generated, there's no need to keep all the data parts loaded or saved.
    public class RiverData {

        public bool fullyGenerated = false;
        public float waterMaxFlow;
        public List<RiverRegion> river; //Always will start with the Sink of the river at list entry 0, all the way to the (primary) Source at the last entry. Null if the River is fully generated.
        public XZ continentalCoords; //The coordinates of the Continental Voronoi Point that this River exists on. Null if the river is fully generated.
        public XZ regionMins;
        public XZ regionMaxs; //This and the mins are just a bundled way of storing the minimum-most (and similarly maximum-most) X and Z values of the regions that this River passes through
                              //Helpful to compare against these min and max values to even see if a region being tested or generated needs to care about this river at all.
        public TreeAttribute specialRiverAttributes; //Potentially used to compound and reference back to if the river has any special conditions anywhere on it's line. Attributes to come as needed!

        public RiverData(XZ contCoords, XZ sinkRegion, float maxFlow) {
            river = new List<RiverRegion>();
            continentalCoords = contCoords;
            regionMins = new XZ();
            regionMaxs = new XZ();
            regionMins.X = regionMaxs.X = sinkRegion.X;
            regionMins.Z = regionMaxs.Z = sinkRegion.Z;
            waterMaxFlow = maxFlow;
        }
    }

    // Two pronged River Map gen?
    // First a general plan on a continental level...
    // Then for each Region that completes CoastMap generation, a more refined plan can be generated for each Region
    //   Will likely have to keep track (on a per-river basis) which chunks are in which Regions
    //
    // The first pass will just record what Regions this River will pass through.
    //   It will exist solely in the RiverData. RiverRegionData? Holds the Region X and Z along with the component parts of the River that can be found in this Region.
    //   Also the forward and back region references.
    // The second pass will focus on each specific region. It will happen only after the Coastal Data exists. Same Pixel size as CoastMap and LandformMap.
    //   Determine the 'start' and 'end' for this Region's data, then populate the RiverRegionData with the individual RiverChunk datas that represent each bit of the River.
    //   A RiverChunk can take up a flexible number of RiverMap Pixels, each Pixel will hold a reference ID value to link back to the RiverChunk in question.
    //      Reference ID can be built from RegionX merged with RegionZ, and then the individual List slot the RiverChunk Occupies in that RiverRegionData
    //   Each RiverChunk links to the 'start' and 'end' RiverChunk it needs to link up with.
}
