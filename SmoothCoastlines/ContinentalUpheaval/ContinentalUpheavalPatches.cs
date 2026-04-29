using HarmonyLib;
using SmoothCoastlines.LandformHeights;
using System;
using System.Collections;
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
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace SmoothCoastlines.ContinentalUpheaval {

    [HarmonyPatch]
    public class ContinentalUpheavalPatches {

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(GenMaps), nameof(GenMaps.initWorldGen))] //Injection point for initializing various maps after GenMaps is done doing the same!
        public static IEnumerable<CodeInstruction> GenMapsInitWorldGenTranspiler(IEnumerable<CodeInstruction> instructions) {
            var codes = new List<CodeInstruction>(instructions);

            int divCount = 0;
            int indexOfUpheavalMapScale = -1;
            FieldInfo oceanScaleField = null;
            int indexOfGetGeoUpheaval = -1;

            for (int i = 0; i < codes.Count; i++) {
                if (divCount == 0 && codes[i].opcode == OpCodes.Div) {
                    divCount++;
                    oceanScaleField = codes[i - 1].operand as FieldInfo;
                    continue;
                }

                if (divCount == 1 && codes[i].opcode == OpCodes.Div) {
                    divCount++;
                    indexOfUpheavalMapScale = i - 1;
                    continue;
                }

                if (divCount == 2 && codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == 873) {
                    indexOfGetGeoUpheaval = i + 4;
                    break;
                }
            }

            var getTerraPretyUpheavalMap = AccessTools.Method(typeof(ContinentalUpheavalHandler), "GetTerraPretyUpheavalMap", new Type[3] { typeof(long), typeof(int), typeof(List<XZ>) });

            if (oceanScaleField != null && indexOfUpheavalMapScale > -1 && indexOfGetGeoUpheaval > -1) {
                var addRequiredLandAtArg = new List<CodeInstruction> {
                    CodeInstruction.LoadArgument(0),
                    new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(GenMaps), "requireLandAt"))
                };

                codes[indexOfUpheavalMapScale].operand = oceanScaleField;
                codes[indexOfGetGeoUpheaval - 4].operand = 1873;
                codes[indexOfGetGeoUpheaval].operand = getTerraPretyUpheavalMap;
                codes.InsertRange(indexOfGetGeoUpheaval, addRequiredLandAtArg);
            } else {
                SmoothCoastlinesModSystem.Logger.Error("Could not patch GenMaps.InitWorldGen, modified Upheaval will not be in effect. More info on cause to follow:");
                if (oceanScaleField == null) {
                    SmoothCoastlinesModSystem.Logger.Error("Could not locate the first Div instruction.");
                } else if (indexOfUpheavalMapScale == -1) {
                    SmoothCoastlinesModSystem.Logger.Error("Could not locate the second Div instruction.");
                } else if (indexOfGetGeoUpheaval == -1) {
                    SmoothCoastlinesModSystem.Logger.Error("Could not locate the default upheaval seed addition of 873. Did it change?");
                }
            }

            return codes.AsEnumerable();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GenMaps), "forceNoUpheavel")]
        public static bool GenMapsForceNoUpheavalPrefix(IMapRegion mapRegion, int regionX, int regionZ, int pad, int regionsize, ForceLandform fl) {
            //This is just to entirely bypass the ForceNoUpheaval call, since with both Smooth Continents and now reworking Upheaval, we can just be sure it isn't happening when it shouldn't. Might reuse this later.
            return false;
        }

        //A series of patches to attempt sinking the overall world-level downwards by one step while keeping the blocks free above it up to world level.
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(GenTerra), "generate")]
        public static IEnumerable<CodeInstruction> GenTerraGenerateTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator) {
            var codes = new List<CodeInstruction>(instructions);

            int indexOfSealevelWorldHeight = -1;
            int indexOfTimesPointNine = -1;
            //int indexOfSetMapChunk = -1;
            //int numDupCalls = 0;
            //int indexOfSetRLZ = -1;

            for (int i = 0; i < codes.Count; i++) {
                /*if (indexOfSetMapChunk == -1 && codes[i].opcode == OpCodes.Stloc_1) {
                    indexOfSetMapChunk = i + 1;
                    continue;
                }

                if (indexOfSetMapChunk > -1 && numDupCalls < 3 && codes[i].opcode == OpCodes.Dup) {
                    numDupCalls++;
                    if (numDupCalls == 3) {
                        indexOfSetRLZ = i;
                    }
                    continue;
                }*/

                if (indexOfSealevelWorldHeight == -1 && codes[i].opcode == OpCodes.Ldc_I4 && (int)codes[i].operand == 256) {
                    indexOfSealevelWorldHeight = i;
                    continue;
                }

                if (indexOfSealevelWorldHeight > -1 && codes[i].opcode == OpCodes.Ldc_R4 && (float)codes[i].operand == 0.9f) {
                    indexOfTimesPointNine = i;
                    break;
                }
            }

            var sub64FromWorldHeight = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldc_I4, 64),
                new CodeInstruction(OpCodes.Sub)
            };

            /*var setMapChunkField = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldloc_0),
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Stfld, AccessTools.Field(typeof(IMapChunk), "mapchunk"))
            };

            var setXAndZField = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldloc_0),
                new CodeInstruction(OpCodes.Ldloc_S, 5),
                new CodeInstruction(OpCodes.Stfld, AccessTools.Field(typeof(int), "rlX")),
                new CodeInstruction(OpCodes.Ldloc_0),
                new CodeInstruction(OpCodes.Ldloc_S, 6),
                new CodeInstruction(OpCodes.Stfld, AccessTools.Field(typeof(int), "rlZ")),
            };*/

            //codes.InsertRange(indexOfTimesPointNine - 1, sub64FromWorldHeight); //Tweaks the Taper Threshold to account for the - 64 to World Height
            codes.InsertRange(indexOfSealevelWorldHeight, sub64FromWorldHeight); //Sets the Oceanicity Factor WorldHeight - 64
            //codes.InsertRange(indexOfSetRLZ, setXAndZField);
            //codes.InsertRange(indexOfSetMapChunk, setMapChunkField);

            return codes.AsEnumerable();
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(GenTerra), nameof(GenTerra.initWorldGen))]
        public static IEnumerable<CodeInstruction> GenTerraInitWorldGenTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator) {
            var codes = new List<CodeInstruction>(instructions);

            int indexOfLoad256F = -1;

            for (int i = 0; i < codes.Count; i++) {
                if (codes[i].opcode == OpCodes.Ldc_R4 && (float)codes[i].operand == 256f) {
                    indexOfLoad256F = i;
                    break;
                }
            }

            var sub64FromWorldHeight = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldc_I4, 64),
                new CodeInstruction(OpCodes.Sub)
            };

            codes[indexOfLoad256F + 14].opcode = OpCodes.Call;
            codes[indexOfLoad256F + 14].operand = AccessTools.Method(typeof(ContinentalUpheavalPatches), "GetConfigurableFrequency");
            codes[indexOfLoad256F + 19].opcode = OpCodes.Call;
            codes[indexOfLoad256F + 19].operand = AccessTools.Method(typeof(ContinentalUpheavalPatches), "GetConfigurablePersistance");
            codes.InsertRange(indexOfLoad256F + 9, sub64FromWorldHeight); //Sets the WorldHeight sent to TerrainOctaves to WorldHeight - 64
            codes.InsertRange(indexOfLoad256F - 1, sub64FromWorldHeight); //Sets the NoiseScale to WorldHeight - 64

            return codes.AsEnumerable();
        }

        public static double GetConfigurableFrequency() {
            return (0.00030618621784789723 * SmoothCoastlinesModSystem.config.terrainNoiseFrequencyMult);
        }

        public static double GetConfigurablePersistance() {
            return SmoothCoastlinesModSystem.config.terrainNoisePersistance;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(GenTerra), nameof(GenTerra.AssetsFinalize))] //This patch drops the SeaLevel down by 64 blocks, which is 1 step on the World Size scale.
        public static IEnumerable<CodeInstruction> GenTerraAssetsFinalizeTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator) {
            var codes = new List<CodeInstruction>(instructions);

            int indexOfLDCR8 = -1;

            for (int i = 0; i < codes.Count; i++) {
                if (codes[i].opcode == OpCodes.Ldc_R8 && (double)codes[i].operand == 0.4313725490196078) {
                    indexOfLDCR8 = i;
                    break;
                }
            }

            var sub64FromWorldHeight = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldc_I4, 64),
                new CodeInstruction(OpCodes.Sub)
            };

            codes.InsertRange(indexOfLDCR8 + 5, sub64FromWorldHeight); //This sets the Sea Level to WorldHeight - 64

            return codes.AsEnumerable();
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(LandformVariant), nameof(LandformVariant.Init))]
        public static IEnumerable<CodeInstruction> LandformVariantInitTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator) {
            var codes = new List<CodeInstruction>(instructions);

            int indexOfCallVirt = -1;

            for (int i = 0; i < codes.Count; i++) {
                if (codes[i].opcode == OpCodes.Callvirt) {
                    indexOfCallVirt = i;
                    break;
                }
            }

            var sub64FromWorldHeight = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldc_I4_S, 64),
                new CodeInstruction(OpCodes.Sub)
            };

            codes.InsertRange(indexOfCallVirt + 1, sub64FromWorldHeight); //Sub 64 from the Worldheight being sent to LerpThresholds

            return codes.AsEnumerable();
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(LandformVariant), "LerpThresholds")]
        public static IEnumerable<CodeInstruction> LandformVariantLerpThresholdsTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator) {
            var codes = new List<CodeInstruction>(instructions);

            int indexOfFirstMapY = -1;
            int indexOfLastMapY = -1;

            for (int i = 0; i < codes.Count; i++) {
                if (codes[i].opcode == OpCodes.Ldarg_1) {
                    indexOfFirstMapY = i;
                    break;
                }
            }

            for (int i = codes.Count - 1; i > 0; i--) {
                if (codes[i].opcode == OpCodes.Ldarg_1) {
                    indexOfLastMapY = i;
                }
            }

            var add64ToWorldHeight = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldc_I4_S, 64),
                new CodeInstruction(OpCodes.Add)
            };

            codes.InsertRange(indexOfLastMapY + 1, add64ToWorldHeight); //Attempting to ensure this lerped thresholds array is able to fit the whole map. Is this going to still function right with uplift? Will have to test with mountains.
            codes.InsertRange(indexOfFirstMapY + 1, add64ToWorldHeight); //Re-adds the 64 to initializing the array and looping through it all so it fits the full world height.

            return codes.AsEnumerable();
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(LandformVariant), "expandOctaves")]
        public static IEnumerable<CodeInstruction> LandformVariantExpandOctavesTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator) {
            var codes = new List<CodeInstruction>(instructions);

            int indexOfCallVirt = -1;

            for (int i = 0; i < codes.Count; i++) {
                if (codes[i].opcode == OpCodes.Callvirt) {
                    indexOfCallVirt = i;
                    break;
                }
            }

            var sub64FromWorldHeight = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldc_I4_S, 64),
                new CodeInstruction(OpCodes.Sub)
            };

            codes.InsertRange(indexOfCallVirt + 1, sub64FromWorldHeight); //Remove 64 from the number of octaves generated by the world height. Helps keep the same shape despite being 64 blocks taller for the world.

            return codes.AsEnumerable();
        }
    }

    [HarmonyPatch]
    public class MoreContinentalUpheavalPatches {

        public static MethodBase TargetMethod() {
            var type = AccessTools.FirstInner(typeof(GenTerra), t => t.Name.Contains("<>c__DisplayClass34_0"));
            var method = AccessTools.FirstMethod(type, m => m.Name.Contains("<generate>b__0"));
            return method;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator) {
            var codes = new List<CodeInstruction>(instructions);

            int ldelemaCount = 0;
            int indexOfOceanicityCompVal = -1;
            var mapsizeField = AccessTools.Field(AccessTools.FirstInner(typeof(GenTerra), t => t.Name.Contains("<>c__DisplayClass34_0")), "mapsizeY");
            var mapsizem2Field = AccessTools.Field(AccessTools.FirstInner(typeof(GenTerra), t => t.Name.Contains("<>c__DisplayClass34_0")), "mapsizeYm2");
            int indexMapsizeField = -1;
            int indexMapsizeM2Field = -1;
            int indexLdelemaRef = -1;

            for (int i = 0; i < codes.Count; i++) {
                if (ldelemaCount == 0 && codes[i].opcode == OpCodes.Ldelema) {
                    ldelemaCount++;
                    continue;
                }

                if (ldelemaCount == 1 && codes[i].opcode == OpCodes.Ldelema) {
                    if (codes[i + 2].opcode == OpCodes.Ldc_R4) {
                        ldelemaCount++;
                        indexOfOceanicityCompVal = i + 2;
                        continue;
                    }
                }

                if (indexOfOceanicityCompVal > -1 && codes[i].opcode == OpCodes.Ldfld && (FieldInfo)codes[i].operand == mapsizem2Field) {
                    indexMapsizeM2Field = i + 1;
                    continue;
                }

                if (indexMapsizeM2Field > -1 && codes[i].opcode == OpCodes.Ldelem_Ref) {
                    indexLdelemaRef = i;
                    continue;
                }

                if (indexLdelemaRef > -1 && codes[i].opcode == OpCodes.Ldfld && (FieldInfo)codes[i].operand == mapsizeField) {
                    indexMapsizeField = i + 1;
                    break;
                }
            }

            var getHeightmapCompMethod = AccessTools.Method(typeof(MoreContinentalUpheavalPatches), "GetHeightmapCompValue", new Type[3] { typeof(int), typeof(int), typeof(float) });
            
            var factorHeightmapAgainstOceanicity = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldloc_2),
                new CodeInstruction(OpCodes.Ldloc_3),
                new CodeInstruction(OpCodes.Ldloc_S, 11),
                new CodeInstruction(OpCodes.Call, getHeightmapCompMethod)
            };

            var sub64FromWorldHeight = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldc_I4_S, 64),
                new CodeInstruction(OpCodes.Sub)
            };

            if (indexOfOceanicityCompVal > -1 && indexMapsizeM2Field > -1 && indexMapsizeField > -1 && indexLdelemaRef > -1) {
                var examineMethod = AccessTools.Method(typeof(MoreContinentalUpheavalPatches), "InjectAndExamineThresholdLerp", new Type[] { typeof(float[]), typeof(double), typeof(int), typeof(float), typeof(float[][]), typeof(double), typeof(double) });
                codes[indexLdelemaRef - 19].opcode = OpCodes.Nop;
                codes[indexLdelemaRef - 18].opcode = OpCodes.Nop;
                codes[indexLdelemaRef - 17].opcode = OpCodes.Nop;
                codes[indexLdelemaRef - 16].opcode = OpCodes.Nop;
                codes[indexLdelemaRef - 15].opcode = OpCodes.Nop;
                codes[indexLdelemaRef - 14].opcode = OpCodes.Nop;
                codes[indexLdelemaRef - 13].opcode = OpCodes.Nop;
                codes[indexLdelemaRef - 12].opcode = OpCodes.Nop;
                codes[indexLdelemaRef - 11].opcode = OpCodes.Nop;
                codes[indexLdelemaRef - 10].opcode = OpCodes.Nop;

                codes[indexLdelemaRef - 8].opcode = OpCodes.Ldloc_S;
                codes[indexLdelemaRef - 8].operand = 7;
                codes[indexLdelemaRef - 7].opcode = OpCodes.Ldloc_S;
                codes[indexLdelemaRef - 7].operand = 22;
                codes[indexLdelemaRef - 6].opcode = OpCodes.Nop;

                codes[indexLdelemaRef - 1].opcode = OpCodes.Ldloc_S;
                codes[indexLdelemaRef - 1].operand = 15;
                codes[indexLdelemaRef].opcode = OpCodes.Ldloc_S;
                codes[indexLdelemaRef].operand = 16;
                codes[indexLdelemaRef + 1].opcode = OpCodes.Nop;
                codes[indexLdelemaRef + 2].opcode = OpCodes.Call;
                codes[indexLdelemaRef + 2].operand = examineMethod;

                codes[indexLdelemaRef + 6].opcode = OpCodes.Nop;
                codes[indexLdelemaRef + 7].opcode = OpCodes.Nop;
                codes[indexLdelemaRef + 8].opcode = OpCodes.Nop;
                codes[indexLdelemaRef + 9].opcode = OpCodes.Nop;
                codes[indexLdelemaRef + 10].opcode = OpCodes.Nop;
                codes[indexLdelemaRef + 11].opcode = OpCodes.Nop;
                codes[indexLdelemaRef + 12].opcode = OpCodes.Nop;
                codes[indexLdelemaRef + 13].opcode = OpCodes.Nop;
                codes[indexLdelemaRef + 14].opcode = OpCodes.Nop;

                //codes.InsertRange(indexMapsizeField, sub64FromWorldHeight); //Sub 64 from GeoUpheaval Mapsize
                codes.InsertRange(indexMapsizeM2Field, sub64FromWorldHeight); //Sub 64 from the StartSampleDisplacedThreshold MapsizeM2
                codes.RemoveAt(indexOfOceanicityCompVal);
                codes.InsertRange(indexOfOceanicityCompVal, factorHeightmapAgainstOceanicity);

                codes.RemoveAt(indexOfOceanicityCompVal - 9); //This block removes the call to ComputeOceanGenAndUpheavalDistY and most of the arguments
                codes.RemoveAt(indexOfOceanicityCompVal - 10);
                codes.RemoveAt(indexOfOceanicityCompVal - 11);
                codes.RemoveAt(indexOfOceanicityCompVal - 12);
                codes.RemoveAt(indexOfOceanicityCompVal - 13);
                codes.RemoveAt(indexOfOceanicityCompVal - 14);
                codes.RemoveAt(indexOfOceanicityCompVal - 16); //Skips the UpheavalStrength argument to keep that, but these two clean up the two this. calls before it.
                codes.RemoveAt(indexOfOceanicityCompVal - 17);
            } else {
                SmoothCoastlinesModSystem.Logger.Error("Transpiler on GenTerra's Generate Lambda Method has failed. Shoving the Sea Water placement closer to the coast will not function.");
                if (ldelemaCount < 1) {
                    SmoothCoastlinesModSystem.Logger.Error("Could not locate first ldelema instruction.");
                } else if (ldelemaCount < 2) {
                    SmoothCoastlinesModSystem.Logger.Error("Could not find the second ldelema call. Only found " + ldelemaCount);
                } else if (indexMapsizeM2Field == -1) {
                    SmoothCoastlinesModSystem.Logger.Error("Could not locate the loading of the MapsizeM2 Field.");
                } else if (indexMapsizeField == -1) {
                    SmoothCoastlinesModSystem.Logger.Error("Could not locate the loading of the Mapsize Field.");
                }
            }

            return codes.AsEnumerable();
        }

        public static float GetHeightmapCompValue(int worldx, int worldz, float oceanicity) {
            return MapLayerLandformsSmooth.noiseLandforms.GetCompValueForOceanicity(worldx, worldz, oceanicity);
        }
        
        public static float InjectAndExamineThresholdLerp(float[] columnLandformIndexedWeights, double threshold, int distortedPosYBase, float distortedPosYSlide, float[][] terrainYThresholds, double noiseMin, double noiseMax) {
            float total = 0;

            for (int i = 0; i < columnLandformIndexedWeights.Length; i++) {
                float weight = columnLandformIndexedWeights[i];
                if (weight == 0) {
                    continue;
                }
                total += weight * GameMath.Lerp(terrainYThresholds[i][distortedPosYBase], terrainYThresholds[i][distortedPosYBase + 1], distortedPosYSlide);
            }

            return total;
        }
    }

    

    [HarmonyPatch]
    public class AttemptSmoothingPatch {

        public static MethodBase TargetMethod() {
            var method = AccessTools.Constructor(typeof(LerpedWeightedIndex2DMap), new Type[] { typeof(int[]), typeof(int), typeof(int), typeof(int), typeof(int) });
            return method;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> LerpedWeightedIndex2DMapConstructorTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilGenerator) {
            var codes = new List<CodeInstruction>(instructions);

            int stfldCount = 0;
            object groupsField = null;
            int indexOfInjectOverride = -1;
            int indexOfBreakTo = -1;

            for (int i = 0; i < codes.Count; i++) {
                if (stfldCount < 4 && codes[i].opcode == OpCodes.Stfld) {
                    stfldCount++;
                    if (stfldCount < 3) {
                        continue;
                    }
                    groupsField = codes[i].operand;
                    continue;
                }

                if (stfldCount == 4 && codes[i].opcode == OpCodes.Div) {
                    indexOfInjectOverride = i + 2;
                    continue;
                }

                if (indexOfInjectOverride > -1 && codes[i].opcode == OpCodes.Endfinally) {
                    indexOfBreakTo = i + 1;
                    break;
                }
            }

            if (indexOfInjectOverride > -1 && indexOfBreakTo > -1) {
                var newLabel = ilGenerator.DefineLabel();
                var lerpMapOverrideMethod = AccessTools.Method(typeof(AttemptSmoothingPatch), "LerpMapConstructorInjection", new Type[] { typeof(int[]), typeof(WeightedIndex[][]), typeof(Dictionary<int, float>), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(float) });

                var callLerpMapOverride = new List<CodeInstruction> {
                    CodeInstruction.LoadArgument(1),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.LoadField(typeof(LerpedWeightedIndex2DMap), "groups"),
                    CodeInstruction.LoadLocal(0),
                    CodeInstruction.LoadArgument(2),
                    CodeInstruction.LoadLocal(1),
                    CodeInstruction.LoadLocal(2),
                    CodeInstruction.LoadLocal(3),
                    CodeInstruction.LoadLocal(4),
                    CodeInstruction.LoadLocal(5),
                    CodeInstruction.LoadLocal(6),
                    CodeInstruction.LoadLocal(7),
                    new CodeInstruction(OpCodes.Call, lerpMapOverrideMethod),
                    new CodeInstruction(OpCodes.Br, newLabel)
                };

                codes[indexOfBreakTo].labels.Add(newLabel);
                codes.InsertRange(indexOfInjectOverride, callLerpMapOverride);
            } else {
                
            }

            return codes.AsEnumerable();
        }

        public static void LerpMapConstructorInjection(int[] rawScalarValues, WeightedIndex[][] groups, Dictionary<int, float> indices, int sizeX, int x, int z, int minx, int minz, int maxx, int maxz, float weightFrac) {
            if (SmoothCoastlinesModSystem.config.enableEdgeLandformSmoothing) {
                bool isEdge = false;
                var curLandform = rawScalarValues[z * sizeX + x];
                var numOtherAdjacent = 0;
                int miniMinX = Math.Max(0, x - 1);
                int miniMinZ = Math.Max(0, z - 1);
                int miniMaxX = Math.Min(sizeX - 1, x + 1); //This 1 is the blur radius - 2 with default settings.
                int miniMaxZ = Math.Min(sizeX - 1, z + 1); //Be sure to send the blurRadius and calc this properly if it works out.
                int chunkThreshold = (((miniMaxX - miniMinX) + 1) * ((miniMaxZ - miniMinZ) + 1)) / 2;

                for (int ax = miniMinX; ax <= miniMaxX; ax++) {
                    for (int az = miniMinZ; az <= miniMaxZ; az++) {
                        if (ax == 0 && az == 0) {
                            continue;
                        }
                        if (curLandform != rawScalarValues[az * sizeX + ax]) {
                            numOtherAdjacent++;
                            if (numOtherAdjacent >= chunkThreshold) {
                                isEdge = true;
                                break;
                            }
                        }
                    }
                    if (isEdge) {
                        break;
                    }
                }

                if (isEdge) {
                    LerpConstructorForEdgeChunk(rawScalarValues, groups, indices, sizeX, x, z, minx, minz, maxx, maxz, weightFrac, chunkThreshold, curLandform);
                } else {
                    LerpConstructorForNormalChunk(rawScalarValues, groups, indices, sizeX, x, z, minx, minz, maxx, maxz, weightFrac);
                }
            } else {
                LerpConstructorForNormalChunk(rawScalarValues, groups, indices, sizeX, x, z, minx, minz, maxx, maxz, weightFrac);
            }

            /*if (isEdge && indices.Count > 1) {
                KeyValuePair<int, float> dominantPair = indices.First();
                KeyValuePair<int, float> secondaryPair = dominantPair;

                foreach (var index in indices) {
                    if (index.Value > dominantPair.Value) {
                        dominantPair = index;
                    }
                    if (index.Value < dominantPair.Value && index.Value > secondaryPair.Value) {
                        secondaryPair = index;
                    }
                }

                if (dominantPair.Key != secondaryPair.Key) {
                    var lostWeight = (dominantPair.Value * 0.42f);
                    indices[dominantPair.Key] -= lostWeight;
                    indices[secondaryPair.Key] += lostWeight;
                }
            }*/
        }

        public static void LerpConstructorForNormalChunk(int[] rawScalarValues, WeightedIndex[][] groups, Dictionary<int, float> indices, int sizeX, int x, int z, int minx, int minz, int maxx, int maxz, float weightFrac) {
            for (int bx = minx; bx <= maxx; bx++) {
                for (int bz = minz; bz <= maxz; bz++) {
                    int index = rawScalarValues[bz * sizeX + bx];

                    if (indices.TryGetValue(index, out float prevValue)) {
                        indices[index] = weightFrac + prevValue;
                    } else {
                        indices[index] = weightFrac;
                    }
                }
            }

            groups[z * sizeX + x] = new WeightedIndex[indices.Count];
            int i = 0;
            foreach (var val in indices) {
                groups[z * sizeX + x][i++] = new WeightedIndex() { Index = val.Key, Weight = val.Value };
            }
        }

        public static void LerpConstructorForEdgeChunk(int[] rawScalarValues, WeightedIndex[][] groups, Dictionary<int, float> indices, int sizeX, int x, int z, int minx, int minz, int maxx, int maxz, float weightFrac, int chunkThreshold, int curLandform) {
            Dictionary<int, int> counts = new Dictionary<int, int>();
            float remainder = 1f;

            for (int bx = minx; bx <= maxx; bx++) {
                for (int bz = minz; bz <= maxz; bz++) {
                    int index = rawScalarValues[bz * sizeX + bx];

                    if (indices.TryGetValue(index, out float prevValue)) {
                        var prevCount = counts[index];
                        prevCount++;
                        counts[index] = prevCount;

                        var fracMult = 0.5f;
                        if (prevCount < chunkThreshold) {
                            fracMult = ((float)chunkThreshold / (float)prevCount);
                        }
                        fracMult = (fracMult * weightFrac);
                        indices[index] = fracMult + prevValue;
                        remainder -= fracMult;
                    } else {
                        indices[index] = weightFrac;
                        remainder -= weightFrac;
                        counts[index] = 1;
                    }
                }
            }

            /*remainder = remainder / indices.Count;
            groups[z * sizeX + x] = new WeightedIndex[indices.Count];
            int i = 0;
            foreach (var val in indices) {
                groups[z * sizeX + x][i++] = new WeightedIndex() { Index = val.Key, Weight = val.Value + remainder };
            }*/

            groups[z * sizeX + x] = new WeightedIndex[indices.Count];
            int i = 0;
            foreach (var val in indices) {
                if (val.Key == curLandform) {
                    groups[z * sizeX + x][i++] = new WeightedIndex() { Index = val.Key, Weight = val.Value + remainder };
                } else {
                    groups[z * sizeX + x][i++] = new WeightedIndex() { Index = val.Key, Weight = val.Value };
                }
            }
        }
    }
}
