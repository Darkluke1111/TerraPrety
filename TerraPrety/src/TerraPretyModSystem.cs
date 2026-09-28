using HarmonyLib;
using MapLayer;
using TerraPrety.LandformHeights;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace TerraPrety;

public class TerraPretyModSystem : ModSystem
{

    public static WorldGenConfig config;

    public Harmony harmony;
    public static ILogger Logger;
    public static ICoreServerAPI Sapi;
    public const int NumBlocksLowerWorldBy = 64;

    public int NoiseSizeRivers;
    public int NoiseSizeCoast;
    public int regionMapSize;
    public MapLayerBase CoastMap;
    public MapLayerBase RiverMap;
    public LandformsWorldProperty landforms;

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Server;
    }

    public override void StartPre(ICoreAPI api)
    {
        Logger = Mod.Logger;
        if (!Harmony.HasAnyPatches(Mod.Info.ModID))
        {
            harmony = new Harmony(Mod.Info.ModID);
            harmony.PatchAll();
        }

    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        Sapi = api;

        TryToLoadConfig(api);

        //TerraGenConfig.landFormSmoothingRadius = config.landformSmoothingRadius;
        //TerraGenConfig.landformMapPadding = config.landformMapPadding;

        api.ChatCommands
            .Create("adjustLandformSmoothing")
            .WithAlias("als")
            .WithDescription("Adjust and set the TerraGenConfig LandformSmoothingRadius, LandformMapPadding automatically handled as well.")
            .RequiresPrivilege("controlserver")
            .WithArgs(api.ChatCommands.Parsers.Int("radius"))
            .HandleWith(args => AdjustLandformSmoothingRadius(api, args));

        api.ChatCommands.Create("terrapretycoastmap")
            .WithDescription("Check the coastmap info where you're at")
            .RequiresPrivilege("controlserver")
            .HandleWith(TerraPretyCoastMapDebug);
    }

    private static TextCommandResult TerraPretyCoastMapDebug(TextCommandCallingArgs args)
    {
        MapLayerOceansSmooth oceanMap = MapLayerOceansSmooth.Instance;
        LandformHeightNoise landformNoise = MapLayerLandformsSmooth.noiseLandforms;
        Entity player = args.Caller.Entity;
        if (oceanMap == null || landformNoise == null || player == null)
            return TextCommandResult.Error("Ocean map or landform noise not ready or no player");

        int playerX = (int)player.Pos.X;
        int playerZ = (int)player.Pos.Z;
        int oceanX = playerX / TerraGenConfig.oceanMapScale;
        int oceanZ = playerZ / TerraGenConfig.oceanMapScale;
        int landformX = playerX / TerraGenConfig.landformMapScale;
        int landformZ = playerZ / TerraGenConfig.landformMapScale;

        return TextCommandResult.Success(
            $"@ X: ={playerX}, Z: ={playerZ}\n" +
            $"Ocean opacity: {oceanMap.OceanOpacity(oceanX, oceanZ):F3} / 1\n" +
            $"Coastmap opacity: {oceanMap.CoastOpacity(oceanX, oceanZ):F3} / 1\n" +
            $"Landform height: {landformNoise.HeightNoiseHeight(landformX, landformZ):F3} / 1\n" +
            $"Landform height after coastmap lowers it: {landformNoise.CoastalMapLoweredHeight(landformX, landformZ):F3} / 1");
    }

    public static WorldGenConfig TryToLoadConfig(ICoreAPI api)
    {
        try
        {
            config = api.LoadModConfig<WorldGenConfig>("TerraPrety.json");
            if (config == null)
            {
                config = new WorldGenConfig();
            }

            api.StoreModConfig<WorldGenConfig>(config, "TerraPrety.json");
        }
        catch (Exception e)
        {
            api.Logger.Error("Could not load config! Loading default settings instead.");
            api.Logger.Error(e);
            config = new WorldGenConfig();
        }
        return config;
    }

    private static TextCommandResult AdjustLandformSmoothingRadius(ICoreServerAPI sapi, TextCommandCallingArgs args) {
        var radius = args[0] as int? ?? 0;

        if (radius < 0) {
            return TextCommandResult.Error("Radius of less than 0 sent. Not proceeding.");
        }

        TerraGenConfig.landFormSmoothingRadius = radius;
        TerraGenConfig.landformMapPadding = radius + 1;

        return TextCommandResult.Success("LandformSmoothingRadius and MapPadding set! Run /wgen again to see the difference.");
    }
}
