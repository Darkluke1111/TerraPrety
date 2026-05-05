using TerraPrety;
using TerraPrety.LandformHeights;
using Vintagestory.API.Common;
using Vintagestory.ServerMods;

public class ContinentalUpheavalHandler
{
	public static void PostGenMapsOnMapRegionGen(IMapRegion mapRegion, int regionX, int regionZ)
	{
		MapLayerLandformsSmooth landformsGen = (MapLayerLandformsSmooth) TerraPretyModSystem.Sapi.ModLoader.GetModSystem<GenMaps>(true).landformsGen;
        landformsGen?.AddHeightmapToRegion(mapRegion);
	}
}
