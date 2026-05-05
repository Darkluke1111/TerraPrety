using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace TerraPrety.LandformHeights {
    public class LandformsHeightsWorldProperty : WorldProperty<LandformGenHeight> {
        [JsonIgnore]
        public LandformGenHeight[] LandformHeightsByIndex;
    }
}
