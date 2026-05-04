using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SmoothCoastlines;
using SmoothCoastlines.LandformHeights;
using Vintagestory.ServerMods;

[HarmonyPatch]
public class MoreContinentalUpheavalPatches
{
    public static Type closureWrapperType = AccessTools.FirstInner(typeof(GenTerra), (Type t) => t.Name.Contains("DisplayClass"));
    public static MethodInfo parallelClosure = AccessTools.FirstMethod(closureWrapperType, (MethodInfo m) => m.Name.Contains("<generate>"));

	public static MethodBase TargetMethod()
    {
        return parallelClosure;
    }
	[HarmonyTranspiler]
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator)
	{

		List<CodeInstruction> list = [.. instructions];
		int num = 0;
		int num2 = -1;
		FieldInfo fieldInfo = AccessTools.Field(closureWrapperType, "mapsizeY");
		FieldInfo fieldInfo2 = AccessTools.Field(closureWrapperType, "mapsizeYm2");
		int num3 = -1;
		int num4 = -1;
		int num5 = -1;
		for (int i = 0; i < list.Count; i++)
		{
			if (num == 0 && list[i].opcode == OpCodes.Ldelema)
			{
				num++;
			}
			else if (num == 1 && list[i].opcode == OpCodes.Ldelema && list[i + 2].opcode == OpCodes.Ldc_R4)
			{
				num++;
				num2 = i + 2;
			}
			else if (num2 > -1 && list[i].opcode == OpCodes.Ldfld && (FieldInfo)list[i].operand == fieldInfo2)
			{
				num4 = i + 1;
			}
			else if (num4 > -1 && list[i].opcode == OpCodes.Ldelem_Ref)
			{
				num5 = i;
			}
			else if (num5 > -1 && list[i].opcode == OpCodes.Ldfld && (FieldInfo)list[i].operand == fieldInfo)
			{
				num3 = i + 1;
				break;
			}
		}

		MethodInfo methodInfo = SymbolExtensions.GetMethodInfo((int worldX, int worldZ, float oceanicity) => GetHeightmapCompValue(worldX, worldZ, oceanicity));
		
		List<CodeInstruction> collection = new List<CodeInstruction>
		{
			new CodeInstruction(OpCodes.Ldloc_2, null),
			new CodeInstruction(OpCodes.Ldloc_3, null),
			new CodeInstruction(OpCodes.Ldloc_S, 11),
			new CodeInstruction(OpCodes.Call, methodInfo)
		};
		new List<CodeInstruction>
		{
			new CodeInstruction(OpCodes.Ldc_I4_S, 64),
			new CodeInstruction(OpCodes.Sub, null)
		};
		if (num2 > -1 && num4 > -1 && num3 > -1 && num5 > -1)
		{
			list.RemoveAt(num2);
			list.InsertRange(num2, collection);
		}
		else
		{
			SmoothCoastlinesModSystem.Logger.Error("Transpiler on GenTerra's Generate Lambda Method has failed. Shoving the Sea Water placement closer to the coast will not function.");
			if (num < 1)
			{
				SmoothCoastlinesModSystem.Logger.Error("Could not locate first ldelema instruction.");
			}
			else if (num < 2)
			{
				SmoothCoastlinesModSystem.Logger.Error("Could not find the second ldelema call. Only found " + num);
			}
			else if (num4 == -1)
			{
				SmoothCoastlinesModSystem.Logger.Error("Could not locate the loading of the MapsizeM2 Field.");
			}
			else if (num3 == -1)
			{
				SmoothCoastlinesModSystem.Logger.Error("Could not locate the loading of the Mapsize Field.");
			}
		}
		return list.AsEnumerable();
	}

	public static float GetHeightmapCompValue(int worldx, int worldz, float oceanicity)
	{
		return MapLayerLandformsSmooth.noiseLandforms.GetCompValueForOceanicity(worldx, worldz, oceanicity);
	}
}
