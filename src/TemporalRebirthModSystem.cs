using HarmonyLib;
using TemporalRebirth.Patches;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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
        // Cosmetic only: without it the death screen shows vanilla's life count.
        TryPatch(api, typeof(DeathDialogPatch));
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
        // Without the respawn patch, extra lives would be shown but vanilla would still refuse the respawn.
        if (!TryPatch(api, typeof(RespawnPatch)))
        {
            LifeRules.Disabled = true;
            api.Logger.Error("[temporalrebirth] Lives, ghosts and rituals are DISABLED until the mod is updated for this game version.");
        }

        TryPatch(api, typeof(TemporalGearPatch));
        DownedCompat.WarnIfUntested(api);
    }

    /// <summary>
    /// Patches one class, logging instead of crashing when a game update renamed the target.
    /// Each patch is independent so one broken target does not take the others down.
    /// </summary>
    private bool TryPatch(ICoreAPI api, Type patchClass)
    {
        try
        {
            harmony!.CreateClassProcessor(patchClass).Patch();
            return true;
        }
        catch (Exception e)
        {
            api.Logger.Error("[temporalrebirth] Could not apply {0} (game version {1}): {2}", patchClass.Name, GameVersion.ShortGameVersion, e.Message);
            return false;
        }
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
