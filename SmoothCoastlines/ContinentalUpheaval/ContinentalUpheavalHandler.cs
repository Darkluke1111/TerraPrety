using SmoothCoastlines.LandformHeights;
using SmoothCoastlines.Rivers;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.ContinentalUpheaval {

    public class ContinentalUpheavalHandler {

        public static MapLayerBase GetTerraPretyUpheavalMap(long seed, int scale, List<XZ> requireLandAt) {
            return new MapLayerContinentalUpheaval(seed, SmoothCoastlinesModSystem.config, requireLandAt);
        }

        public static void PostGenMapsInitWorldGen(ICoreServerAPI sapi) { //This can actually serve as an injection point for allowing the init of other Maps after GenMaps is finished with it's Init.
            var genRivers = sapi.ModLoader.GetModSystem<GenRivers>();
            genRivers.InitWorldGenPostGenMaps(sapi);
        }

        public static void PostGenMapsOnMapRegionGen(IMapRegion mapRegion, int regionX, int regionZ) {
            var sapi = SmoothCoastlinesModSystem.Sapi;
            var genMaps = sapi.ModLoader.GetModSystem<GenMaps>();
            var genRivers = sapi.ModLoader.GetModSystem<GenRivers>();

            ((MapLayerLandformsSmooth)genMaps.landformsGen)?.AddHeightmapToRegion(mapRegion);
            genRivers.OnMapRegionGenPostGenMaps(mapRegion, regionX, regionZ);

            /*int cpad = 5;
            var noiseSizeOcean = genMaps.noiseSizeOcean;
            CoastMap coastMap = sapi.ModLoader.GetModSystem<GenRivers>().CoastMap as CoastMap;
            coastMap.SetRequiredMaps(mapRegion.OceanMap, mapRegion.ModMaps["LandformHeightMap"]);
            var coastData = coastMap.GenLayer(regionX * noiseSizeOcean - cpad, regionZ * noiseSizeOcean - cpad, noiseSizeOcean + 2 * cpad, noiseSizeOcean + 2 * cpad);
            var coastDataMap = new IntDataMap2D {
                Data = coastData,
                Size = noiseSizeOcean + 2*cpad,
                TopLeftPadding = cpad,
                BottomRightPadding = cpad
            };
            mapRegion.ModMaps["CoastlineAndWaterTypeMap"] = coastDataMap;*/
        }

        public static float GetContinentalAndMountainUpheavalForArea(IMapChunk mapChunk, int rlX, int rlZ, int lX, int lZ) { //Did I use this anywhere...? I don't think so since it had the old ModMaps and that was never actually set or used.
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
