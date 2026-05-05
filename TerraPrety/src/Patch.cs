using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MapLayer;
using TerraPrety.LandformHeights;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace TerraPrety
{
    [HarmonyPatch]
    public static class Patch
    {
        //This patch switches the vanilla MapOceanGen class for the custom MapOceanGenSmooth class
        [HarmonyPrefix]
        [HarmonyPatch(typeof(GenMaps), nameof(GenMaps.GetOceanMapGen))]
        public static bool Prefix(ref MapLayerBase __result, long seed, float landcover, int oceanMapScale, float oceanScaleMul, List<XZ> requireLandAt, bool requiresSpawnOffset)
        {
            __result = new MapLayerOceansSmooth(seed, TerraPretyModSystem.config, requireLandAt);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GenMaps), nameof(GenMaps.GetLandformMapGen))]
        public static bool Prefix(ref MapLayerBase __result, long seed, NoiseClimate climateNoise, ICoreServerAPI api, float landformScale)
        {
            new MapLayerLandforms(seed + 12, climateNoise, api, landformScale); //Luke pointed out this is a much better place to initialize this, since it SHOULD be the same as in the SmoothLandforms version, this should be fine! Both init them the same way, Smooth just also inits the heights as well.
            MapLayerLandformsSmooth mapLayerLandformsSmooth = new MapLayerLandformsSmooth(seed + 12, climateNoise, api, landformScale, TerraPretyModSystem.config);
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
            TerraPretyModSystem.Logger.Debug($"Forcing land at {positionX}, {positionZ} with radius {radius} and region size {regionSize}");
            var factor = __instance.noiseSizeOcean / regionSize;
            __instance.requireLandAt.Add(new XZ((int)(positionX * factor), (int)(positionZ * factor)));

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GenMaps), nameof(GenMaps.ForceLandformAt))]
        public static bool Prefix(GenMaps __instance, ForceLandform landform)
        {
            if (__instance.landformsGen is MapLayerLandformsSmooth)
            {
                ((MapLayerLandformsSmooth)__instance.landformsGen).AddForcedLandform(landform);
            }

            return true;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(GenMaps), "OnMapRegionGen")]
        public static IEnumerable<CodeInstruction> OnMapRegionGenTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = [.. instructions];
            int num = -1;
            int num2 = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (num == -1 && list[i].opcode == OpCodes.Ldc_I4_3)
                {
                    num = i;
                }
                else if (num > -1 && list[i].opcode == OpCodes.Ldstr && list[i].operand as string == "forceLandform")
                {
                    num2 = i - 3;
                    break;
                }
            }
            MethodInfo methodInfo = AccessTools.Method(typeof(ContinentalUpheavalHandler), "PostGenMapsOnMapRegionGen",[typeof(IMapRegion),typeof(int),typeof(int)], null);
            List<CodeInstruction> collection = new List<CodeInstruction>
        {
            CodeInstruction.LoadArgument(1, false),
            CodeInstruction.LoadArgument(2, false),
            CodeInstruction.LoadArgument(3, false),
            new CodeInstruction(OpCodes.Call, methodInfo)
        };
            if (num > -1 && num2 > -1)
            {
                list.InsertRange(num2, collection);
            }
            return list.AsEnumerable();
        }
    }
}
