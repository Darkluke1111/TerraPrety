using HarmonyLib;
using MapLayer;
using TerraPrety.LandformHeights;
using MapLayer;
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

    const string ConfigFileName = "TerraPrety.json";
    const string BakedConfigSaveKey = "terraprety-worldgen-config";

    public Harmony harmony;
    public static ILogger Logger;
    public static ICoreServerAPI Sapi;

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

        api.ChatCommands.Create("terrapretybakeconfig")
            .WithAlias("tpbakeconfig")
            .WithDescription("Copy TerraPrety.json over this world's saved settings. Add confirm to actually do it, then restart before generating new chunks.")
            .RequiresPrivilege("controlserver")
            .WithArgs(api.ChatCommands.Parsers.OptionalWord("confirm"))
            .HandleWith(BakeConfigFromFile);
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
        // First load copies our json into the world, affter that the baked copy wins.
        var fromFile = ReadConfigFile(api);
        var sapi = api as ICoreServerAPI;
        if (sapi == null)
        {
            config = fromFile ?? new WorldGenConfig();
            return config;
        }

        byte[] saved = sapi.WorldManager.SaveGame.GetData(BakedConfigSaveKey);
        if (saved != null && saved.Length > 0)
        {
            try
            {
                config = JsonUtil.FromBytes<WorldGenConfig>(saved);
                if (config != null)
                {
                    // this has been added mainly for debugging purposes, to see if the noise scale is being applied correctly.
                    var fileScale = fromFile != null ? fromFile.noiseScale.ToString() : "unreadable";
                    api.Logger.Notification($"Using TerraPretty settings that are baked in the save, are actually? Check -> noiseScale {config.noiseScale}, the .json has {fileScale}");
                    return config;
                }
                api.Logger.Error("Terraprety settings in this world save were empty, let's leave them alone");
            }
            catch (Exception e)
            {
                api.Logger.Error("Couldn't read TerraPrety settings from this world save, don't mess with them then");
                api.Logger.Error(e);
            }

            config = fromFile ?? new WorldGenConfig();
            return config;
        }

        if (fromFile == null)
        {
            api.Logger.Error("Couldn't write settings into this save, brother we are using defaults until the .json loads");
            config = new WorldGenConfig();
            return config;
        }

        sapi.WorldManager.SaveGame.StoreData(BakedConfigSaveKey, JsonUtil.ToBytes(fromFile));
        api.Logger.Notification("Baked TerraPrety.json in this world save, later edits to that file won't change this world, be safe out there");
        config = fromFile;
        return config;
    }

    private static TextCommandResult BakeConfigFromFile(TextCommandCallingArgs args)
    {
        var confirm = args.ArgCount > 0 ? args[0] as string : null;
        if (!string.Equals(confirm, "confirm", StringComparison.OrdinalIgnoreCase))
        {
            return TextCommandResult.Error("Overwrites this world baked TerraPrety settings with TerraPrety.json, old chunks stay. Better to restart before generating new ones. Run /terrapretybakeconfig confirm");
        }

        WorldGenConfig fromFile;
        try
        {
            fromFile = Sapi.LoadModConfig<WorldGenConfig>("TerraPrety.json");
        }
        catch (Exception e)
        {
            Sapi.Logger.Error("Couldn't load config, save left alone");
            Sapi.Logger.Error(e);
            return TextCommandResult.Error("Couldn't read TerraPrety.json, save left alone.");
        }

        if (fromFile == null)
            return TextCommandResult.Error("TerraPrety.json isn't there, uh? save left alone.");

        Sapi.WorldManager.SaveGame.StoreData(BakedConfigSaveKey, JsonUtil.ToBytes(fromFile));
        return TextCommandResult.Success("Baked TerraPrety.json in this world, better to restart before generating new chunks");
    }

    // null means the file was there but didn't parse. A missing file comes back as defaults and gets written out.
    private static WorldGenConfig ReadConfigFile(ICoreAPI api)
    {
        try
        {
            var loaded = api.LoadModConfig<WorldGenConfig>(ConfigFileName);
            if (loaded == null) loaded = new WorldGenConfig();
            api.StoreModConfig(loaded, ConfigFileName);
            return loaded;
        }
        catch (Exception e)
        {
            api.Logger.Error("Could not load config!");
            api.Logger.Error(e);
            return null;
        }
    }

    private static TextCommandResult AdjustLandformSmoothingRadius(ICoreServerAPI sapi, TextCommandCallingArgs args)
    {
        var radius = args[0] as int? ?? 0;

        if (radius < 0)
        {
            return TextCommandResult.Error("Radius of less than 0 sent. Not proceeding.");
        }

        TerraGenConfig.landFormSmoothingRadius = radius;
        TerraGenConfig.landformMapPadding = radius + 1;

        return TextCommandResult.Success("LandformSmoothingRadius and MapPadding set! Run /wgen again to see the difference.");
    }
}
