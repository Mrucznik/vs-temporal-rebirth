using HarmonyLib;
using TemporalRebirth.Patches;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace TemporalRebirth;

public sealed class TemporalRebirthModSystem : ModSystem
{
    private const string HarmonyId = "temporalrebirth";
    private const string ConfigFile = "temporalrebirth.json";

    // Harmony patches are static; they reach the server state through these.
    internal static RebirthConfig Config { get; private set; } = new();
    internal static Rebirth? Rebirth { get; private set; }

    private Harmony? harmony;
    private ICoreServerAPI? sapi;
    private long returnTickId;
    private long tetherTickId;

    public override void StartClientSide(ICoreClientAPI api)
    {
        harmony = new Harmony(HarmonyId + ".client");
        harmony.CreateClassProcessor(typeof(DeathDialogPatch)).Patch();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        Config = LoadConfig(api);

        var afterlife = new Afterlife(api);
        Rebirth = new Rebirth(api, Config, afterlife);

        api.Event.SaveGameLoaded += afterlife.Load;
        api.Event.SaveGameLoaded += () => LifeRules.CurrentEpoch = Epochs.Advance(api);
        api.Event.GameWorldSave += afterlife.Save;
        api.Event.PlayerNowPlaying += Rebirth.OnPlayerNowPlaying;
        api.Event.PlayerDeath += Rebirth.OnPlayerDeath;
        returnTickId = api.Event.RegisterGameTickListener(_ => Rebirth.CheckReturns(), 30_000);
        tetherTickId = api.Event.RegisterGameTickListener(_ => Rebirth.EnforceTethers(), 1_000);

        Commands.Register(api, Rebirth);

        harmony = new Harmony(HarmonyId + ".server");
        harmony.CreateClassProcessor(typeof(RespawnPatch)).Patch();
        harmony.CreateClassProcessor(typeof(TemporalGearPatch)).Patch();
    }

    private static RebirthConfig LoadConfig(ICoreServerAPI api)
    {
        RebirthConfig? config = null;
        try
        {
            config = api.LoadModConfig<RebirthConfig>(ConfigFile);
        }
        catch (Exception e)
        {
            api.Logger.Error("[temporalrebirth] {0} is invalid, using defaults: {1}", ConfigFile, e.Message);
        }

        config ??= new RebirthConfig();
        // Writes back new keys after an update.
        api.StoreModConfig(config, ConfigFile);
        return config;
    }

    public override void Dispose()
    {
        if (sapi != null && Rebirth != null)
        {
            sapi.Event.SaveGameLoaded -= Rebirth.Afterlife.Load;
            sapi.Event.GameWorldSave -= Rebirth.Afterlife.Save;
            sapi.Event.PlayerNowPlaying -= Rebirth.OnPlayerNowPlaying;
            sapi.Event.PlayerDeath -= Rebirth.OnPlayerDeath;
            sapi.Event.UnregisterGameTickListener(returnTickId);
            sapi.Event.UnregisterGameTickListener(tetherTickId);
        }

        harmony?.UnpatchAll(harmony.Id);
        harmony = null;
        Rebirth = null;
        sapi = null;
    }
}
