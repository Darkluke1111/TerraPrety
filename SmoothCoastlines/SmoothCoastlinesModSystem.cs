using Vintagestory.API.Server;
using Vintagestory.API.Common;
using HarmonyLib;
using System;
using Vintagestory.API.MathTools;
using MapLayer;
using Vintagestory.ServerMods;
using Vintagestory.GameContent;
using System.Collections.Generic;

namespace SmoothCoastlines;

public class SmoothCoastlinesModSystem : ModSystem
{

    public static WorldGenConfig config;

    public Harmony harmony;
    public static ILogger Logger;
    public static ICoreServerAPI Sapi;

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
