using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace TerraPrety.LandformHeights {

    public class LandformGenHeight : WorldPropertyVariant {
        [JsonIgnore]
        public int index;

        [JsonProperty]
        public double minHeight = 0;

        [JsonProperty]
        public double maxHeight = 1;

        public void Init(int index) {
            this.index = index;
        }
    }
}
