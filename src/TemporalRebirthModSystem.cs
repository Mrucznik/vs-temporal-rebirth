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

    // Harmony patches are static; these are all they can reach. One process runs both sides in singleplayer.
    internal static ServerParts? Server { get; private set; }
    internal static LeashGuard? ClientLeash { get; private set; }

    private Harmony? harmony;
    private ICoreServerAPI? sapi;
    private ICoreClientAPI? capi;
    private Zones? zones;
    private TimerHud? timer;
    private readonly List<IRenderer> renderers = new();
    private long returnTickId;
    private long limitTickId;

    public override void StartClientSide(ICoreClientAPI api)
    {
        harmony = new Harmony(HarmonyId + ".client");
        // Cosmetic only: without it the death screen shows vanilla's life count.
        TryPatch(api, typeof(DeathDialogPatch));
        capi = api;
        var guard = new LeashGuard(api);
        ClientLeash = guard;
        guard.Start();
        // Without it only the server holds players, with jerks.
        TryPatch(api, typeof(LeashStepPatch));
        new ZombieLook(api).Start();
        var ghosts = new Ghosts(api);
        ghosts.Start();
        new WispLook(api, ghosts).Start();
        new ZonePrediction(api).Start();
        timer = new TimerHud(api);
        // After the assets: shaders are among them.
        api.Event.BlockTexturesLoaded += () =>
        {
            renderers.Add(new LeashLook(api, guard));
            renderers.Add(new EchoLook(api, ghosts));
            renderers.Add(new WispOrb(api, ghosts));
        };
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        var file = new ConfigFile(Path.Combine(GamePaths.ModConfig, ConfigFileName), text => api.Logger.Error("[temporalrebirth] {0}", text));
        RebirthConfig config = file.Load();
        WarnAboutMissing(api, config);

        var afterlife = new Afterlife(api);
        var messages = new Messages(api, config);
        var control = new PlayerControl(api);
        zones = new Zones(api, config, afterlife);
        var places = new Places(api, config, afterlife, zones);
        var leash = new Leash(api, config, zones, messages);
        var bans = new Bans(api, config, afterlife, messages);
        var returns = new Returns(api, config, afterlife, control, places, leash, messages);
        var rebirth = new Rebirth(api, config, afterlife, control, places, leash, bans, returns, messages);
        var rituals = new Rituals(api, config, afterlife, returns, messages);
        var zombies = new ZombieRules(api, config, afterlife);
        Server = new ServerParts(config, afterlife, rebirth, bans, rituals, messages, zombies);

        api.Event.SaveGameLoaded += afterlife.Load;
        api.Event.SaveGameLoaded += () => afterlife.PinOldEntries(config.Snapshot());
        api.Event.SaveGameLoaded += () => LifeRules.CurrentEpoch = Epochs.Advance(api);
        api.Event.GameWorldSave += afterlife.Save;
        api.Event.PlayerNowPlaying += rebirth.OnPlayerNowPlaying;
        api.Event.PlayerDeath += rebirth.OnPlayerDeath;
        api.Event.PlayerRespawn += rebirth.OnPlayerRespawn;
        returnTickId = api.Event.RegisterGameTickListener(_ => rebirth.CheckReturns(), 5_000);
        limitTickId = api.Event.RegisterGameTickListener(_ => rebirth.EnforceLimits(), 1_000);

        Rights.Register(api);
        zones.Register();
        zombies.Register();
        new Commands(api, config, file, afterlife, returns, rituals, zones).Register();

        harmony = new Harmony(HarmonyId + ".server");
        // Without the respawn patch, extra lives would be shown but vanilla would still refuse the respawn.
        if (!TryPatch(api, typeof(RespawnPatch)))
        {
            LifeRules.Disabled = true;
            api.Logger.Error("[temporalrebirth] Lives, ghosts and rituals are DISABLED until the mod is updated for this game version.");
        }

        TryPatch(api, typeof(TemporalGearPatch));
        TryPatch(api, typeof(ZombiePickupPatch));
        TryPatch(api, typeof(ProtectionPatch));
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

    /// <summary>Settings that need something this server does not have.</summary>
    private static void WarnAboutMissing(ICoreServerAPI api, RebirthConfig config)
    {
        if (config.LivesCount != LivesCount.Deaths && !DownedCompat.IsInstalled(api))
        {
            api.Logger.Warning("[temporalrebirth] Lives.Count {0} needs the Downed mod; deaths take lives.", config.LivesCount);
        }
    }

    public override void Dispose()
    {
        if (sapi != null && Server is { } server)
        {
            sapi.Event.SaveGameLoaded -= server.Afterlife.Load;
            sapi.Event.GameWorldSave -= server.Afterlife.Save;
            sapi.Event.PlayerNowPlaying -= server.Rebirth.OnPlayerNowPlaying;
            sapi.Event.PlayerDeath -= server.Rebirth.OnPlayerDeath;
            sapi.Event.PlayerRespawn -= server.Rebirth.OnPlayerRespawn;
            zones?.Unregister();
            sapi.Event.UnregisterGameTickListener(returnTickId);
            sapi.Event.UnregisterGameTickListener(limitTickId);
            Server = null;
        }

        if (capi != null)
        {
            ClientLeash = null;
            renderers.ForEach(r => r.Dispose());
            renderers.Clear();
            timer?.Dispose();
        }

        harmony?.UnpatchAll(harmony.Id);
        harmony = null;
        sapi = null;
        capi = null;
    }
}

/// <summary>The parts of the server side that Harmony patches need.</summary>
internal sealed record ServerParts(RebirthConfig Config, Afterlife Afterlife, Rebirth Rebirth, Bans Bans, Rituals Rituals, Messages Messages, ZombieRules Zombies);
