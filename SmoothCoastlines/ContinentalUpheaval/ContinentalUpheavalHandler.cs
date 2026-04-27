using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.ContinentalUpheaval {

    public class ContinentalUpheavalHandler {

        public static MapLayerBase GetTerraPretyUpheavalMap(long seed, int scale, List<XZ> requireLandAt) {
            return new MapLayerContinentalUpheaval(seed, SmoothCoastlinesModSystem.config, requireLandAt);
        }

        /*public static void HandleGenMapsForAddedMaps(IMapRegion mapRegion, int regionX, int regionZ) {
            int opad = 5;
            var noiseSizeOcean = SmoothCoastlinesModSystem.Sapi.WorldManager.RegionSize / TerraGenConfig.oceanMapScale;
            var conUpheavalData = SmoothCoastlinesModSystem.Sapi.ModLoader.GetModSystem<SmoothCoastlinesModSystem>().continentalUpheavalMap.GenLayer(regionX * noiseSizeOcean - opad, regionZ * noiseSizeOcean - opad, noiseSizeOcean + 2 * opad, noiseSizeOcean + 2 * opad);
            var conUpheavalMap = new IntDataMap2D {
                Data = conUpheavalData,
                Size = noiseSizeOcean + 2*opad,
                TopLeftPadding = opad,
                BottomRightPadding = opad
            };

            mapRegion.ModMaps["ContinentalUpheavalMap"] = conUpheavalMap;
        }*/

        public static float GetContinentalAndMountainUpheavalForArea(IMapChunk mapChunk, int rlX, int rlZ, int lX, int lZ) {
            float retVal = 0;
            var contUpheavalMap = mapChunk.MapRegion.ModMaps["ContinentalUpheavalMap"];
            int regionChunkSize = SmoothCoastlinesModSystem.Sapi.WorldManager.RegionSize / GlobalConstants.ChunkSize;
            const float chunkBlockDelta = 1.0f / GlobalConstants.ChunkSize;

            if (contUpheavalMap != null && contUpheavalMap.Data.Length > 0) {
                float ofac = (float)contUpheavalMap.InnerSize / regionChunkSize;
                int contUpheavalUpLeft = contUpheavalMap.GetUnpaddedInt((int)(rlX * ofac), (int)(rlZ * ofac));
                int contUpheavalUpRight = contUpheavalMap.GetUnpaddedInt((int)(rlX * ofac + ofac), (int)(rlZ * ofac));
                int contUpheavalBotLeft = contUpheavalMap.GetUnpaddedInt((int)(rlX * ofac), (int)(rlZ * ofac + ofac));
                int contUpheavalBotRight = contUpheavalMap.GetUnpaddedInt((int)(rlX * ofac + ofac), (int)(rlZ * ofac + ofac));

                float continentalLift = GameMath.BiLerp(contUpheavalUpLeft, contUpheavalUpRight, contUpheavalBotLeft, contUpheavalBotRight, lX * chunkBlockDelta, lZ * chunkBlockDelta);
                retVal += continentalLift;
            }

            return retVal;
        }
    }
}
