using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace SmoothCoastlines.Rivers {

    //This represents a singular River. The WHOLE of the river, of which each part can be in various states of completeness.
    //To ease up on the RAM usage, any part that is connected on both ends to completely generated parts can be cleared and replaced with a placeholder chunk - to keep the List in order.
    //This is true for the full river itself as well. If it's fully generated, there's no need to keep all the data parts loaded or saved.
    public class RiverData {

        public bool fullyGenerated = false;
        public float waterMaxFlow;
        public Dictionary<XZ, RiverRegion> river; //Null if the River is fully generated. The key is the Region Coords for the region.
        public XZ continentalCenterCoords; //The coordinates of the region that is at the "center" of this Veronoi point. Null if the river is fully generated.
        
        public TreeAttribute specialRiverAttributes; //Potentially used to compound and reference back to if the river has any special conditions anywhere on it's line. Attributes to come as needed!

        public RiverData(XZ centerCoords, float maxFlow) {
            river = new Dictionary<XZ, RiverRegion>();
            continentalCenterCoords = centerCoords;
            waterMaxFlow = maxFlow;
            specialRiverAttributes = new TreeAttribute();
        }

        public void AddRegionToRiver(RiverRegion region) {
            river.Add(region.regionCoords, region);
        }

        public void RemoveRegionFromRiver(RiverRegion region) {
            river.Remove(region.regionCoords);
        }

        public bool DoesRiverPassThroughRegion(XZ regionCoords) {
            return river.ContainsKey(regionCoords);
        }

        public bool DoesRiverPassThroughMapTile(XZ regionCoords, XZ worldCoords) {
            if (river.TryGetValue(regionCoords, out RiverRegion region)) {
                return region.DoesRiverPassThroughMapTile(worldCoords);
            } else {
                return false;
            }
        }

        public RiverRegion GetRegionAt(XZ regionCoords) {
            return river[regionCoords];
        }
    }

    public struct RiverPlottingChances {
        public bool NorthEastFree = true;
        public bool NorthFree = true;
        public bool NorthWestFree = true;
        public bool WestFree = true;
        public bool EastFree = true;
        public bool SouthWestFree = true;
        public bool SouthFree = true;
        public bool SouthEastFree = true;

        public float NorthWestHeight = 0;
        public float NorthHeight = 0;
        public float NorthEastHeight = 0;
        public float WestHeight = 0;
        public float EastHeight = 0;
        public float SouthWestHeight = 0;
        public float SouthHeight = 0;
        public float SouthEastHeight = 0;

        public int NorthWestOceanicity = 0;
        public int NorthOceanicity = 0;
        public int NorthEastOceanicity = 0;
        public int WestOceanicity = 0;
        public int EastOceanicity = 0;
        public int SouthWestOceanicity = 0;
        public int SouthOceanicity = 0;
        public int SouthEastOceanicity = 0;

        public int NorthWestWeight;
        public int NorthWeight;
        public int NorthEastWeight;
        public int WestWeight;
        public int EastWeight;
        public int SouthWestWeight;
        public int SouthWeight;
        public int SouthEastWeight;

        public RiverPlottingChances(int baseWeight) {
            NorthWestWeight = baseWeight;
            NorthWeight = baseWeight;
            NorthEastWeight = baseWeight;
            WestWeight = baseWeight;
            EastWeight = baseWeight;
            SouthWestWeight = baseWeight;
            SouthWeight = baseWeight;
            SouthEastWeight = baseWeight;
        }
    }

    //Primary River Logic is based on trying to go for as long as possible while keeping the minimum elevation increase for each step.
    public class PrimaryRiverLogic : RiverPlottingLogic {

        private float primaryHeightMapFlex;

        public PrimaryRiverLogic(int baseWeight) : base(baseWeight) {
            primaryHeightMapFlex = SmoothCoastlinesModSystem.config.primaryRiverHeightStepFlex;
        }

        public override RiverPlottingLogic ChainLogic(int baseWeight) {
            return new PrimaryRiverLogic(baseWeight);
        }

        public override RiverPlottingLogic ShiftLogic(int baseWeight) {
            return new TributaryRiverLogic(baseWeight);
        }

        public override void SetHeightOfDir(XZ dir, float dirHeight, float curHeight) {
            var heightWithFlexFactor = dirHeight + heightFlexFactor;
            var heightDiff = Math.Abs(dirHeight - curHeight);

            if (dir.Z != 0) {
                if (dir.Z < 0) {
                    if (dir.X < 0 && chances.NorthWestFree) {
                        if (heightWithFlexFactor < curHeight || heightDiff > primaryHeightMapFlex) {
                            chances.NorthWestFree = false;
                            chances.NorthWestWeight = 0;
                        } else {
                            /*if (heightDiff > higherLimit) {
                                chances.NorthWestWeight /= (heightDiff - higherLimit);
                            } else*/ if (heightDiff > 0) {
                                chances.NorthWestWeight = (int)(chances.NorthWestWeight * ((float)(primaryHeightMapFlex - heightDiff) / primaryHeightMapFlex));
                            }
                            chances.NorthWestHeight = dirHeight;
                        }
                    } else if (dir.X > 0 && chances.NorthEastFree) {
                        if (heightWithFlexFactor < curHeight || heightDiff > primaryHeightMapFlex) {
                            chances.NorthEastFree = false;
                            chances.NorthEastWeight = 0;
                        } else {
                            /*if (heightDiff > higherLimit) {
                                chances.NorthEastWeight /= (heightDiff - higherLimit);
                            } else*/ if (heightDiff > 0) {
                                chances.NorthEastWeight = (int)(chances.NorthEastWeight * ((float)(primaryHeightMapFlex - heightDiff) / primaryHeightMapFlex));
                            }
                            chances.NorthEastHeight = dirHeight;
                        }
                    } else if (chances.NorthFree) {
                        if (heightWithFlexFactor < curHeight || heightDiff > primaryHeightMapFlex) {
                            chances.NorthFree = false;
                            chances.NorthWeight = 0;
                        } else {
                            /*if (heightDiff > higherLimit) {
                                chances.NorthWeight /= (heightDiff - higherLimit);
                            } else*/ if (heightDiff > 0) {
                                chances.NorthWeight = (int)(chances.NorthWeight * ((float)(primaryHeightMapFlex - heightDiff) / primaryHeightMapFlex));
                            }
                            chances.NorthHeight = dirHeight;
                        }
                    }
                } else {
                    if (dir.X < 0 && chances.SouthWestFree) {
                        if (heightWithFlexFactor < curHeight || heightDiff > primaryHeightMapFlex) {
                            chances.SouthWestFree = false;
                            chances.SouthWestWeight = 0;
                        } else {
                            /*if (heightDiff > higherLimit) {
                                chances.SouthWestWeight /= (heightDiff - higherLimit);
                            } else*/ if (heightDiff > 0) {
                                chances.SouthWestWeight = (int)(chances.SouthWestWeight * ((float)(primaryHeightMapFlex - heightDiff) / primaryHeightMapFlex));
                            }
                            chances.SouthWestHeight = dirHeight;
                        }
                    } else if (dir.X > 0 && chances.SouthEastFree) {
                        if (heightWithFlexFactor < curHeight || heightDiff > primaryHeightMapFlex) {
                            chances.SouthEastFree = false;
                            chances.SouthEastWeight = 0;
                        } else {
                            /*if (heightDiff > higherLimit) {
                                chances.SouthEastWeight /= (heightDiff - higherLimit);
                            } else*/ if (heightDiff > 0) {
                                chances.SouthEastWeight = (int)(chances.SouthEastWeight * ((float)(primaryHeightMapFlex - heightDiff) / primaryHeightMapFlex));
                            }
                            chances.SouthEastHeight = dirHeight;
                        }
                    } else if (chances.SouthFree) {
                        if (heightWithFlexFactor < curHeight || heightDiff > primaryHeightMapFlex) {
                            chances.SouthFree = false;
                            chances.SouthWeight = 0;
                        } else {
                            /*if (heightDiff > higherLimit) {
                                chances.SouthWeight /= (heightDiff - higherLimit);
                            } else*/ if (heightDiff > 0) {
                                chances.SouthWeight = (int)(chances.SouthWeight * ((float)(primaryHeightMapFlex - heightDiff) / primaryHeightMapFlex));
                            }
                            chances.SouthHeight = dirHeight;
                        }
                    }
                }
            } else {
                if (dir.X < 0 && chances.WestFree) {
                    if (heightWithFlexFactor < curHeight || heightDiff > primaryHeightMapFlex) {
                        chances.WestFree = false;
                        chances.WestWeight = 0;
                    } else {
                        /*if (heightDiff > higherLimit) {
                            chances.WestWeight /= (heightDiff - higherLimit);
                        } else*/ if (heightDiff > 0) {
                            chances.WestWeight = (int)(chances.WestWeight * ((float)(primaryHeightMapFlex - heightDiff) / primaryHeightMapFlex));
                        }
                        chances.WestHeight = dirHeight;
                    }
                } else if (chances.EastFree) {
                    if (heightWithFlexFactor < curHeight || heightDiff > primaryHeightMapFlex) {
                        chances.EastFree = false;
                        chances.EastWeight = 0;
                    } else {
                        /*if (heightDiff > higherLimit) {
                            chances.EastWeight /= (heightDiff - higherLimit);
                        } else*/ if (heightDiff > 0) {
                            chances.EastWeight = (int)(chances.EastWeight * ((float)(primaryHeightMapFlex - heightDiff) / primaryHeightMapFlex));
                        }
                        chances.EastHeight = dirHeight;
                    }
                }
            }
        }
    }

    //Tributary River Logic is based on trying to make crazy choices and find the higher points, try and make something interesting and find an end point.
    public class TributaryRiverLogic : RiverPlottingLogic {

        private float tributaryMaxStep;

        public TributaryRiverLogic(int baseWeight) : base(baseWeight) {
            tributaryMaxStep = SmoothCoastlinesModSystem.config.tributaryDesiredHeightStepUp;
        }

        public override RiverPlottingLogic ChainLogic(int baseWeight) {
            return new TributaryRiverLogic(baseWeight);
        }

        public override RiverPlottingLogic ShiftLogic(int baseWeight) {
            return null;
        }

        public override void SetHeightOfDir(XZ dir, float dirHeight, float curHeight) {
            var halfFlex = (heightFlexFactor / 2);
            var fullTributaryFracStep = tributaryMaxStep + halfFlex; //The full distance the step's height can be from the current height
            var heightWithFlexFactor = dirHeight + halfFlex;
            var heightFromTarget = (curHeight + tributaryMaxStep) - dirHeight; //A negative value here means that dirHeight is higher then the target height. A positive value means it is lower.

            if (dir.Z != 0) {
                if (dir.Z < 0) {
                    if (dir.X < 0 && chances.NorthWestFree) {
                        if (heightWithFlexFactor < curHeight) {
                            chances.NorthWestFree = false;
                            chances.NorthWestWeight = 0;
                        } else {
                            if (heightFromTarget > 0) { //This will catch any dirHeights that are lower then the current target step.
                                chances.NorthWestWeight = (int)(chances.NorthWestWeight * ((float)(fullTributaryFracStep - heightFromTarget) / fullTributaryFracStep));
                            } else if (heightFromTarget < 0 && heightFromTarget < (0 - heightFlexFactor)) { //dirHeight is higher then current target step, but not above the target step height + the flex factor in distance above
                                chances.NorthWestWeight = (int)(chances.NorthWestWeight * (((((float)(tributaryMaxStep - Math.Abs(heightFromTarget)) / tributaryMaxStep)) * 0.6) + 0.4));
                            } else if (heightFromTarget != 0) {  //This catches the rest that do not hit the above two cases, and applies the constant modifier after
                                chances.NorthWestWeight = (int)(chances.NorthWestWeight * 0.4);
                            }
                            chances.NorthWestHeight = dirHeight;
                        }
                    } else if (dir.X > 0 && chances.NorthEastFree) {
                        if (heightWithFlexFactor < curHeight) {
                            chances.NorthEastFree = false;
                            chances.NorthEastWeight = 0;
                        } else {
                            if (heightFromTarget > 0) {
                                chances.NorthEastWeight = (int)(chances.NorthEastWeight * ((float)(fullTributaryFracStep - heightFromTarget) / fullTributaryFracStep));
                            } else if (heightFromTarget < 0 && heightFromTarget < (0 - heightFlexFactor)) {
                                chances.NorthEastWeight = (int)(chances.NorthEastWeight * (((((float)(tributaryMaxStep - Math.Abs(heightFromTarget)) / tributaryMaxStep)) * 0.6) + 0.4));
                            } else if (heightFromTarget != 0) {
                                chances.NorthEastWeight = (int)(chances.NorthEastWeight * 0.4);
                            }
                            chances.NorthEastHeight = dirHeight;
                        }
                    } else if (chances.NorthFree) {
                        if (heightWithFlexFactor < curHeight) {
                            chances.NorthFree = false;
                            chances.NorthWeight = 0;
                        } else {
                            if (heightFromTarget > 0) {
                                chances.NorthWeight = (int)(chances.NorthWeight * ((float)(fullTributaryFracStep - heightFromTarget) / fullTributaryFracStep));
                            } else if (heightFromTarget < 0 && heightFromTarget < (0 - heightFlexFactor)) {
                                chances.NorthWeight = (int)(chances.NorthWeight * (((((float)(tributaryMaxStep - Math.Abs(heightFromTarget)) / tributaryMaxStep)) * 0.6) + 0.4));
                            } else if (heightFromTarget != 0) {
                                chances.NorthWeight = (int)(chances.NorthWeight * 0.4);
                            }
                            chances.NorthHeight = dirHeight;
                        }
                    }
                } else {
                    if (dir.X < 0 && chances.SouthWestFree) {
                        if (heightWithFlexFactor < curHeight) {
                            chances.SouthWestFree = false;
                            chances.SouthWestWeight = 0;
                        } else {
                            if (heightFromTarget > 0) {
                                chances.SouthWestWeight = (int)(chances.SouthWestWeight * ((float)(fullTributaryFracStep - heightFromTarget) / fullTributaryFracStep));
                            } else if (heightFromTarget < 0 && heightFromTarget < (0 - heightFlexFactor)) {
                                chances.SouthWestWeight = (int)(chances.SouthWestWeight * (((((float)(tributaryMaxStep - Math.Abs(heightFromTarget)) / tributaryMaxStep)) * 0.6) + 0.4));
                            } else if (heightFromTarget != 0) {
                                chances.SouthWestWeight = (int)(chances.SouthWestWeight * 0.4);
                            }
                            chances.SouthWestHeight = dirHeight;
                        }
                    } else if (dir.X > 0 && chances.SouthEastFree) {
                        if (heightWithFlexFactor < curHeight) {
                            chances.SouthEastFree = false;
                            chances.SouthEastWeight = 0;
                        } else {
                            if (heightFromTarget > 0) {
                                chances.SouthEastWeight = (int)(chances.SouthEastWeight * ((float)(fullTributaryFracStep - heightFromTarget) / fullTributaryFracStep));
                            } else if (heightFromTarget < 0 && heightFromTarget < (0 - heightFlexFactor)) {
                                chances.SouthEastWeight = (int)(chances.SouthEastWeight * (((((float)(tributaryMaxStep - Math.Abs(heightFromTarget)) / tributaryMaxStep)) * 0.6) + 0.4));
                            } else if (heightFromTarget != 0) {
                                chances.SouthEastWeight = (int)(chances.SouthEastWeight * 0.4);
                            }
                            chances.SouthEastHeight = dirHeight;
                        }
                    } else if (chances.SouthFree) {
                        if (heightWithFlexFactor < curHeight) {
                            chances.SouthFree = false;
                            chances.SouthWeight = 0;
                        } else {
                            if (heightFromTarget > 0) {
                                chances.SouthWeight = (int)(chances.SouthWeight * ((float)(fullTributaryFracStep - heightFromTarget) / fullTributaryFracStep));
                            } else if (heightFromTarget < 0 && heightFromTarget < (0 - heightFlexFactor)) {
                                chances.SouthWeight = (int)(chances.SouthWeight * (((((float)(tributaryMaxStep - Math.Abs(heightFromTarget)) / tributaryMaxStep)) * 0.6) + 0.4));
                            } else if (heightFromTarget != 0) {
                                chances.SouthWeight = (int)(chances.SouthWeight * 0.4);
                            }
                            chances.SouthHeight = dirHeight;
                        }
                    }
                }
            } else {
                if (dir.X < 0 && chances.WestFree) {
                    if (heightWithFlexFactor < curHeight) {
                        chances.WestFree = false;
                        chances.WestWeight = 0;
                    } else {
                        if (heightFromTarget > 0) {
                            chances.WestWeight = (int)(chances.WestWeight * ((float)(fullTributaryFracStep - heightFromTarget) / fullTributaryFracStep));
                        } else if (heightFromTarget < 0 && heightFromTarget < (0 - heightFlexFactor)) {
                            chances.WestWeight = (int)(chances.WestWeight * (((((float)(tributaryMaxStep - Math.Abs(heightFromTarget)) / tributaryMaxStep)) * 0.6) + 0.4));
                        } else if (heightFromTarget != 0) {
                            chances.WestWeight = (int)(chances.WestWeight * 0.4);
                        }
                        chances.WestHeight = dirHeight;
                    }
                } else if (chances.EastFree) {
                    if (heightWithFlexFactor < curHeight) {
                        chances.EastFree = false;
                        chances.EastWeight = 0;
                    } else {
                        if (heightFromTarget > 0) {
                            chances.EastWeight = (int)(chances.EastWeight * ((float)(fullTributaryFracStep - heightFromTarget) / fullTributaryFracStep));
                        } else if (heightFromTarget < 0 && heightFromTarget < (0 - heightFlexFactor)) {
                            chances.EastWeight = (int)(chances.EastWeight * (((((float)(tributaryMaxStep - Math.Abs(heightFromTarget)) / tributaryMaxStep)) * 0.6) + 0.4));
                        } else if (heightFromTarget != 0) {
                            chances.EastWeight = (int)(chances.EastWeight * 0.4);
                        }
                        chances.EastHeight = dirHeight;
                    }
                }
            }
        }
    }

    public abstract class RiverPlottingLogic {

        public RiverPlottingChances chances;
        protected int oceanicityFlexFactor;
        protected int riverMaxOceanicity;
        protected float heightFlexFactor;

        public RiverPlottingLogic(int baseWeight) {
            chances = new RiverPlottingChances(baseWeight);
            oceanicityFlexFactor = SmoothCoastlinesModSystem.config.riverOceanicityStepFlexibility;
            riverMaxOceanicity = SmoothCoastlinesModSystem.config.maximumRiverOceanicity;
            heightFlexFactor = SmoothCoastlinesModSystem.config.tributaryRiverHeightStepFlex;
        }

        public abstract RiverPlottingLogic ChainLogic(int baseWeight);

        //This will either return null, in which case it should end entirely, or can be used to transfer one logic to another kind. IE: Primary to Tributary.
        public abstract RiverPlottingLogic ShiftLogic(int baseWeight);

        public virtual RiverPoint ChooseNextPointDirection(RiverPoint currentPoint, int stepBlocks, int rng) {
            //Just run through all possible directions, and here we go...
            if (chances.NorthWestFree) {
                rng -= chances.NorthWestWeight;
                if (rng < 0) {
                    chances.NorthWestFree = false;
                    chances.NorthWestWeight = 0;
                    XZ newXZ = new XZ(currentPoint.worldX + (RiverMap.NorthWest.X * stepBlocks), currentPoint.worldZ + (RiverMap.NorthWest.Z * stepBlocks));
                    return new RiverPoint(newXZ.X, chances.NorthWestHeight, newXZ.Z, chances.NorthWestOceanicity);
                }
            }

            if (chances.NorthFree) {
                rng -= chances.NorthWeight;
                if (rng < 0) {
                    chances.NorthFree = false;
                    chances.NorthWeight = 0;
                    XZ newXZ = new XZ(currentPoint.worldX + (RiverMap.North.X * stepBlocks), currentPoint.worldZ + (RiverMap.North.Z * stepBlocks));
                    return new RiverPoint(newXZ.X, chances.NorthHeight, newXZ.Z, chances.NorthOceanicity);
                }
            }

            if (chances.NorthEastFree) {
                rng -= chances.NorthEastWeight;
                if (rng < 0) {
                    chances.NorthEastFree = false;
                    chances.NorthEastWeight = 0;
                    XZ newXZ = new XZ(currentPoint.worldX + (RiverMap.NorthEast.X * stepBlocks), currentPoint.worldZ + (RiverMap.NorthEast.Z * stepBlocks));
                    return new RiverPoint(newXZ.X, chances.NorthEastHeight, newXZ.Z, chances.NorthEastOceanicity);
                }
            }

            if (chances.WestFree) {
                rng -= chances.WestWeight;
                if (rng < 0) {
                    chances.WestFree = false;
                    chances.WestWeight = 0;
                    XZ newXZ = new XZ(currentPoint.worldX + (RiverMap.West.X * stepBlocks), currentPoint.worldZ + (RiverMap.West.Z * stepBlocks));
                    return new RiverPoint(newXZ.X, chances.WestHeight, newXZ.Z, chances.WestOceanicity);
                }
            }

            if (chances.EastFree) {
                rng -= chances.EastWeight;
                if (rng < 0) {
                    chances.EastFree = false;
                    chances.EastWeight = 0;
                    XZ newXZ = new XZ(currentPoint.worldX + (RiverMap.East.X * stepBlocks), currentPoint.worldZ + (RiverMap.East.Z * stepBlocks));
                    return new RiverPoint(newXZ.X, chances.EastHeight, newXZ.Z, chances.EastOceanicity);
                }
            }

            if (chances.SouthWestFree) {
                rng -= chances.SouthWestWeight;
                if (rng < 0) {
                    chances.SouthWestFree = false;
                    chances.SouthWestWeight = 0;
                    XZ newXZ = new XZ(currentPoint.worldX + (RiverMap.SouthWest.X * stepBlocks), currentPoint.worldZ + (RiverMap.SouthWest.Z * stepBlocks));
                    return new RiverPoint(newXZ.X, chances.SouthWestHeight, newXZ.Z, chances.SouthWestOceanicity);
                }
            }

            if (chances.SouthFree) {
                rng -= chances.SouthWeight;
                if (rng < 0) {
                    chances.SouthFree = false;
                    chances.SouthWeight = 0;
                    XZ newXZ = new XZ(currentPoint.worldX + (RiverMap.South.X * stepBlocks), currentPoint.worldZ + (RiverMap.South.Z * stepBlocks));
                    return new RiverPoint(newXZ.X, chances.SouthHeight, newXZ.Z, chances.SouthOceanicity);
                }
            }

            if (chances.SouthEastFree) { //Ideally this shouldn't ever be false. But JUST IN CASE, there is the fallback below, with error logging.
                rng -= chances.SouthEastWeight;
                if (rng < 0) {
                    chances.SouthEastFree = false;
                    chances.SouthEastWeight = 0;
                    XZ newXZ = new XZ(currentPoint.worldX + (RiverMap.SouthEast.X * stepBlocks), currentPoint.worldZ + (RiverMap.SouthEast.Z * stepBlocks));
                    return new RiverPoint(newXZ.X, chances.SouthEastHeight, newXZ.Z, chances.SouthEastOceanicity);
                }
            }

            SmoothCoastlinesModSystem.Logger.Error("Somehow we managed to fail to choose a proper point despite protections against this. Returning the currentPoint instead.");
            return currentPoint;
        }

        public void PreventDirectionChoice(XZ dir) {
            if (dir.Z != 0) {
                if (dir.Z < 0) {
                    if (dir.X < 0) {
                        chances.NorthWestFree = false;
                        chances.NorthWestWeight = 0;
                    } else if (dir.X > 0) {
                        chances.NorthEastFree = false;
                        chances.NorthEastWeight = 0;
                    } else {
                        chances.NorthFree = false;
                        chances.NorthWeight = 0;
                    }
                } else {
                    if (dir.X < 0) {
                        chances.SouthWestFree = false;
                        chances.SouthWestWeight = 0;
                    } else if (dir.X > 0) {
                        chances.SouthEastFree = false;
                        chances.SouthEastWeight = 0;
                    } else {
                        chances.SouthFree = false;
                        chances.SouthWeight = 0;
                    }
                }
            } else {
                if (dir.X < 0) {
                    chances.WestFree = false;
                    chances.WestWeight = 0;
                } else {
                    chances.EastFree = false;
                    chances.EastWeight = 0;
                }
            }
        }

        public virtual void PreventDoubleBack(XZ dir) {
            if (dir.Z != 0) {
                if (dir.Z < 0) {
                    if (dir.X < 0) {
                        chances.NorthWestFree = false;
                        chances.NorthWestWeight = 0;
                        chances.WestWeight /= 8;
                        chances.NorthWeight /= 8;
                        chances.NorthEastWeight /= 4;
                        chances.SouthWestWeight /= 4;
                        chances.EastWeight /= 2;
                        chances.SouthWeight /= 2;
                    } else if (dir.X > 0) {
                        chances.NorthEastFree = false;
                        chances.NorthEastWeight = 0;
                        chances.EastWeight /= 8;
                        chances.NorthWeight /= 8;
                        chances.NorthWestWeight /= 4;
                        chances.SouthEastWeight /= 4;
                        chances.WestWeight /= 2;
                        chances.SouthWeight /= 2;
                    } else {
                        chances.NorthFree = false;
                        chances.NorthWeight = 0;
                        chances.NorthWestWeight /= 8;
                        chances.NorthEastWeight /= 8;
                        chances.WestWeight /= 4;
                        chances.EastWeight /= 4;
                        chances.SouthWestWeight /= 2;
                        chances.SouthEastWeight /= 2;
                    }
                } else {
                    if (dir.X < 0) {
                        chances.SouthWestFree = false;
                        chances.SouthWestWeight = 0;
                        chances.WestWeight /= 8;
                        chances.SouthWeight /= 8;
                        chances.NorthWestWeight /= 4;
                        chances.SouthEastWeight /= 4;
                        chances.NorthWeight /= 2;
                        chances.EastWeight /= 2;
                    } else if (dir.X > 0) {
                        chances.SouthEastFree = false;
                        chances.SouthEastWeight = 0;
                        chances.EastWeight /= 8;
                        chances.SouthWeight /= 8;
                        chances.SouthWestWeight /= 4;
                        chances.NorthEastWeight /= 4;
                        chances.NorthWeight /= 2;
                        chances.WestWeight /= 2;
                    } else {
                        chances.SouthFree = false;
                        chances.SouthWeight = 0;
                        chances.SouthWestWeight /= 8;
                        chances.SouthEastWeight /= 8;
                        chances.WestWeight /= 4;
                        chances.EastWeight /= 4;
                        chances.NorthWestWeight /= 2;
                        chances.NorthEastWeight /= 2;
                    }
                }
            } else {
                if (dir.X < 0) {
                    chances.WestFree = false;
                    chances.WestWeight = 0;
                    chances.NorthWestWeight /= 8;
                    chances.SouthWestWeight /= 8;
                    chances.NorthWeight /= 4;
                    chances.SouthWeight /= 4;
                    chances.NorthEastWeight /= 2;
                    chances.SouthEastWeight /= 2;
                } else {
                    chances.EastFree = false;
                    chances.EastWeight = 0;
                    chances.NorthEastWeight /= 8;
                    chances.SouthEastWeight /= 8;
                    chances.NorthWeight /= 4;
                    chances.SouthWeight /= 4;
                    chances.NorthWestWeight /= 2;
                    chances.SouthWestWeight /= 2;
                }
            }
        }

        public virtual void SetOceanicityOfDir(XZ dir, int dirOceanicity, int currentPointOceanicity) {
            var oceanicityWithFlexFactor = dirOceanicity - oceanicityFlexFactor;

            if (dir.Z != 0) {
                if (dir.Z < 0) {
                    if (dir.X < 0 && chances.NorthWestFree) {
                        if (oceanicityWithFlexFactor > currentPointOceanicity) {
                            chances.NorthWestFree = false;
                            chances.NorthWestWeight = 0;
                        } else {
                            chances.NorthWestOceanicity = dirOceanicity;
                            if (dirOceanicity > 0) {
                                float mult = (float)(riverMaxOceanicity - dirOceanicity) / riverMaxOceanicity;
                                chances.NorthWestWeight = (int)(chances.NorthWestWeight * mult);
                            }
                        }
                    } else if (dir.X > 0 && chances.NorthEastFree) {
                        if (oceanicityWithFlexFactor > currentPointOceanicity) {
                            chances.NorthEastFree = false;
                            chances.NorthEastWeight = 0;
                        } else {
                            chances.NorthEastOceanicity = dirOceanicity;
                            if (dirOceanicity > 0) {
                                float mult = (float)(riverMaxOceanicity - dirOceanicity) / riverMaxOceanicity;
                                chances.NorthEastWeight = (int)(chances.NorthEastWeight * mult);
                            }
                        }
                    } else if (chances.NorthFree) {
                        if (oceanicityWithFlexFactor > currentPointOceanicity) {
                            chances.NorthFree = false;
                            chances.NorthWeight = 0;
                        } else {
                            chances.NorthOceanicity = dirOceanicity;
                            if (dirOceanicity > 0) {
                                float mult = (float)(riverMaxOceanicity - dirOceanicity) / riverMaxOceanicity;
                                chances.NorthWeight = (int)(chances.NorthWeight * mult);
                            }
                        }
                    }
                } else {
                    if (dir.X < 0 && chances.SouthWestFree) {
                        if (oceanicityWithFlexFactor > currentPointOceanicity) {
                            chances.SouthWestFree = false;
                            chances.SouthWestWeight = 0;
                        } else {
                            chances.SouthWestOceanicity = dirOceanicity;
                            if (dirOceanicity > 0) {
                                float mult = (float)(riverMaxOceanicity - dirOceanicity) / riverMaxOceanicity;
                                chances.SouthWestWeight = (int)(chances.SouthWestWeight * mult);
                            }
                        }
                    } else if (dir.X > 0 && chances.SouthEastFree) {
                        if (oceanicityWithFlexFactor > currentPointOceanicity) {
                            chances.SouthEastFree = false;
                            chances.SouthEastWeight = 0;
                        } else {
                            chances.SouthEastOceanicity = dirOceanicity;
                            if (dirOceanicity > 0) {
                                float mult = (float)(riverMaxOceanicity - dirOceanicity) / riverMaxOceanicity;
                                chances.SouthEastWeight = (int)(chances.SouthEastWeight * mult);
                            }
                        }
                    } else if (chances.SouthFree) {
                        if (oceanicityWithFlexFactor > currentPointOceanicity) {
                            chances.SouthFree = false;
                            chances.SouthWeight = 0;
                        } else {
                            chances.SouthOceanicity = dirOceanicity;
                            if (dirOceanicity > 0) {
                                float mult = (float)(riverMaxOceanicity - dirOceanicity) / riverMaxOceanicity;
                                chances.SouthWeight = (int)(chances.SouthWeight * mult);
                            }
                        }
                    }
                }
            } else {
                if (dir.X < 0 && chances.WestFree) {
                    if (oceanicityWithFlexFactor > currentPointOceanicity) {
                        chances.WestFree = false;
                        chances.WestWeight = 0;
                    } else {
                        chances.WestOceanicity = dirOceanicity;
                        if (dirOceanicity > 0) {
                            float mult = (float)(riverMaxOceanicity - dirOceanicity) / riverMaxOceanicity;
                            chances.WestWeight = (int)(chances.WestWeight * mult);
                        }
                    }
                } else if (chances.EastFree) {
                    if (oceanicityWithFlexFactor > currentPointOceanicity) {
                        chances.EastFree = false;
                        chances.EastWeight = 0;
                    } else {
                        chances.EastOceanicity = dirOceanicity;
                        if (dirOceanicity > 0) {
                            float mult = (float)(riverMaxOceanicity - dirOceanicity) / riverMaxOceanicity;
                            chances.EastWeight = (int)(chances.EastWeight * mult);
                        }
                    }
                }
            }
        }

        public virtual void SetHeightOfDir(XZ dir, float dirHeight, float curHeight) {
            if (dir.Z != 0) {
                if (dir.Z < 0) {
                    if (dir.X < 0) {
                        chances.NorthWestHeight = dirHeight;
                    } else if (dir.X > 0) {
                        chances.NorthEastHeight = dirHeight;
                    } else {
                        chances.NorthHeight = dirHeight;
                    }
                } else {
                    if (dir.X < 0) {
                        chances.SouthWestHeight = dirHeight;
                    } else if (dir.X > 0) {
                        chances.SouthEastHeight = dirHeight;
                    } else {
                        chances.SouthHeight = dirHeight;
                    }
                }
            } else {
                if (dir.X < 0) {
                    chances.WestHeight = dirHeight;
                } else {
                    chances.EastHeight = dirHeight;
                }
            }
        }

        public int GetFullChance() {
            return chances.NorthWestWeight + chances.NorthWeight + chances.NorthEastWeight + chances.WestWeight + chances.EastWeight + chances.SouthWestWeight + chances.SouthWeight + chances.SouthEastWeight;
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
