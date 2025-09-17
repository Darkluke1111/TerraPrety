using Cairo;
using HarmonyLib;
using MapLayer;
using SmoothCoastlines.ContinentalUpheaval;
using SmoothCoastlines.LandformHeights;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Vintagestory.Server;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace SmoothCoastlines
{
    [HarmonyPatch]
    public static class Patch
    {
        //This patch switches the vanilla MapOceanGen class for the custom MapOceanGenSmooth class
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GenMaps), nameof(GenMaps.GetOceanMapGen))]
        public static bool Prefix(ref MapLayerBase __result, long seed, float landcover, int oceanMapScale, float oceanScaleMul, List<XZ> requireLandAt, bool requiresSpawnOffset)
        {
            __result = new MapLayerOceansSmooth(seed, SmoothCoastlinesModSystem.config, requireLandAt);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GenMaps), nameof(GenMaps.GetLandformMapGen))]
        public static bool Prefix(ref MapLayerBase __result, long seed, NoiseClimate climateNoise, ICoreServerAPI api, float landformScale) {
            new MapLayerLandforms(seed + 12, climateNoise, api, landformScale); //Luke pointed out this is a much better place to initialize this, since it SHOULD be the same as in the SmoothLandforms version, this should be fine! Both init them the same way, Smooth just also inits the heights as well.
            MapLayerLandformsSmooth mapLayerLandformsSmooth = new MapLayerLandformsSmooth(seed + 12, climateNoise, api, landformScale, SmoothCoastlinesModSystem.config);
            mapLayerLandformsSmooth.DebugDrawBitmap(DebugDrawMode.LandformRGB, 0, 0, "Height-Based Landforms");
            __result = mapLayerLandformsSmooth;
            
            return false;
        }

        //This patch changes the way that GenMaps generates the list of areas where the ocean map is forced to have land (for spawn and story structures) because MapOceanGenSmooth does this stuff differently than the vanilla one.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GenMaps), "ForceRandomLandArea")]
        public static bool Prefix(GenMaps __instance, int positionX, int positionZ, int radius)
        {
            var sapi = (ICoreServerAPI)__instance.GetType().GetField("sapi", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance);
            double regionSize = sapi.WorldManager.RegionSize;
            var factor =  __instance.noiseSizeOcean / regionSize;
            __instance.requireLandAt.Add(new XZ((int) (positionX * factor), (int) (positionZ * factor)));
            
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GenMaps), nameof(GenMaps.ForceLandformAt))]
        public static bool Prefix(GenMaps __instance, ForceLandform landform) {
            if (__instance.landformsGen is MapLayerLandformsSmooth) {
                ((MapLayerLandformsSmooth)__instance.landformsGen).AddForcedLandform(landform);
            }

            return true;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(GenMaps), "OnMapRegionGen")]
        public static IEnumerable<CodeInstruction> OnMapRegionGenTranspiler(IEnumerable<CodeInstruction> instructions) {
            var codes = new List<CodeInstruction>(instructions);

            int indexOfUpPad = -1;
            int indexOfHeightMapInjectPoint = -1;

            for (int i = 0; i < codes.Count; i++) {
                if (indexOfUpPad == -1 && codes[i].opcode == OpCodes.Ldc_I4_3) {
                    indexOfUpPad = i;
                    continue;
                }

                if (indexOfUpPad > -1 && codes[i].opcode == OpCodes.Ldstr && codes[i].operand as string == "forceLandform") {
                    indexOfHeightMapInjectPoint = i - 3;
                    break;
                }
            }

            //var addHeightmapToRegionMethod = AccessTools.Method(typeof(Patch), "AddHeightmapToRegionData", new Type[1] { typeof(IMapRegion) });
            var injectionForPostGenMapsOnMapRegionGen = AccessTools.Method(typeof(ContinentalUpheavalHandler), "PostGenMapsOnMapRegionGen", new Type[3] { typeof(IMapRegion), typeof(int), typeof(int) });

            /*var addHeightmapToRegionData = new List<CodeInstruction> {
                CodeInstruction.LoadArgument(1),
                new CodeInstruction(OpCodes.Call, addHeightmapToRegionMethod)
            };*/

            var injectAdditionalMapData = new List<CodeInstruction> {
                CodeInstruction.LoadArgument(1),
                CodeInstruction.LoadArgument(2),
                CodeInstruction.LoadArgument(3),
                new CodeInstruction(OpCodes.Call, injectionForPostGenMapsOnMapRegionGen)
            };

            if (indexOfUpPad > -1 && indexOfHeightMapInjectPoint > -1) {
                codes[indexOfUpPad].opcode = OpCodes.Ldc_I4_5; //This is changing 'upPad' for the upheaval padding in GenMaps to equal that of the OceanMap.
                codes.InsertRange(indexOfHeightMapInjectPoint, injectAdditionalMapData); //Send all the argument data to this method to add additional modded map data to the Region.
                //codes.InsertRange(indexOfHeightMapInjectPoint, addHeightmapToRegionData); //Heightmap map data added to the above call as well!
            } else {
                SmoothCoastlinesModSystem.Logger.Warning("GenMaps.OnMapRegionGen transpiler has failed. Will not be able to save the Heightmap to Region Data, and upheaval padding is incorrect.");
            }

            return codes.AsEnumerable();
        }

        /*private static void AddHeightmapToRegionData(IMapRegion region) {
            ((MapLayerLandformsSmooth)SmoothCoastlinesModSystem.Sapi.ModLoader.GetModSystem<GenMaps>().landformsGen)?.AddHeightmapToRegion(region);
        }*/
    }
}
