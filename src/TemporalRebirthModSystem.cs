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
    private const string ConfigFileName = "temporalrebirth.json";

    // Harmony patches are static; they reach the server state through these.
    internal static RebirthConfig Config { get; private set; } = RebirthConfig.Default();
    internal static Rebirth? Rebirth { get; private set; }
    internal static Rituals? Rituals { get; private set; }
    internal static Messages? Messages { get; private set; }

    private Harmony? harmony;
    private ICoreServerAPI? sapi;
    private long returnTickId;
    private long tetherTickId;

    public override void StartClientSide(ICoreClientAPI api)
    {
        harmony = new Harmony(HarmonyId + ".client");
        // Cosmetic only: without it the death screen shows vanilla's life count.
        TryPatch(api, typeof(DeathDialogPatch));
        new ZombieLook(api).Start();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        var file = new ConfigFile(Path.Combine(GamePaths.ModConfig, ConfigFileName), text => api.Logger.Error("[temporalrebirth] {0}", text));
        Config = file.Load();
        WarnAboutUnfinished(api);

        var afterlife = new Afterlife(api);
        Messages = new Messages(api, Config);
        Rebirth = new Rebirth(api, Config, afterlife, Messages);
        Rituals = new Rituals(api, Config, afterlife, Rebirth, Messages);

        api.Event.SaveGameLoaded += afterlife.Load;
        api.Event.SaveGameLoaded += () => LifeRules.CurrentEpoch = Epochs.Advance(api);
        api.Event.GameWorldSave += afterlife.Save;
        api.Event.PlayerNowPlaying += Rebirth.OnPlayerNowPlaying;
        api.Event.PlayerDeath += Rebirth.OnPlayerDeath;
        returnTickId = api.Event.RegisterGameTickListener(_ => Rebirth.CheckReturns(), 5_000);
        tetherTickId = api.Event.RegisterGameTickListener(_ => Rebirth.EnforceTethers(), 1_000);

        Rights.Register(api);
        new Commands(api, Config, file, Rebirth, Rituals).Register();
        new ZombieRules(api, afterlife, Rebirth).Register();

        harmony = new Harmony(HarmonyId + ".server");
        // Without the respawn patch, extra lives would be shown but vanilla would still refuse the respawn.
        if (!TryPatch(api, typeof(RespawnPatch)))
        {
            LifeRules.Disabled = true;
            api.Logger.Error("[temporalrebirth] Lives, ghosts and rituals are DISABLED until the mod is updated for this game version.");
        }

        TryPatch(api, typeof(TemporalGearPatch));
        TryPatch(api, typeof(ZombiePickupPatch));
        if (!TryPatch(api, typeof(JoinPatch)))
        {
            api.Logger.Error("[temporalrebirth] Bans do not work until the mod is updated for this game version: players are disconnected but can rejoin.");
        }
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

    /// <summary>Settings the configurator already offers but this version does not do yet.</summary>
    private static void WarnAboutUnfinished(ICoreServerAPI api)
    {
        if (Config.PlaceAt == Place.Zone)
        {
            api.Logger.Warning("[temporalrebirth] Place.At Zone is not implemented yet; using the marked place with Place.Radius.");
        }

        if (Config.LivesCount != "Deaths")
        {
            api.Logger.Warning("[temporalrebirth] Lives.Count {0} is not implemented yet; deaths take lives.", Config.LivesCount);
        }
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
        Rituals = null;
        Messages = null;
        ZombieRules.Current = null;
        sapi = null;
    }
}
