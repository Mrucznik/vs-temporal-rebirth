using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace TemporalRebirth;

/// <summary>
/// Client side of rituals: what the server says a gear would do here (for the hint and to refuse a hold that cannot
/// work), the look and sound of a hold that is a ritual, and the chooser. The server decides everything again.
/// </summary>
internal class RitualClient
{
    private const long RefreshMs = 250;
    private const float ShakePerStep = 0.02f;

    private readonly ICoreClientAPI api;
    private readonly IClientNetworkChannel channel;
    private readonly RitualProgressHud progress;
    private RitualHintPacket? hint;
    private long hintAtMs;
    private (RitualState, bool, int)? shownKey;
    private RitualChooser? chooser;

    // The hold in progress, when it is a ritual.
    private Vec3d? holdAt;
    private ILoadedSound? sound;
    private long lastMoteMs;

    public RitualClient(ICoreClientAPI api)
    {
        this.api = api;
        progress = new RitualProgressHud(api);
        channel = api.Network.RegisterChannel(RitualNet.Channel)
            .RegisterMessageType<RitualHintPacket>()
            .RegisterMessageType<ChooserOpenPacket>()
            .RegisterMessageType<ChooserClosePacket>()
            .RegisterMessageType<ChooserPickPacket>()
            .SetMessageHandler<RitualHintPacket>(OnHint)
            .SetMessageHandler<ChooserOpenPacket>(OnOpen)
            .SetMessageHandler<ChooserClosePacket>(_ => chooser?.Dismiss());
        api.Event.RegisterGameTickListener(_ => Refresh(), (int)RefreshMs);
        api.Event.LeaveWorld += () =>
        {
            EndHold();
            hint = null;
        };
    }

    public bool Holding => holdAt != null;

    private static bool IsGear(ItemSlot? slot) => slot?.Itemstack?.Collectible is ItemTemporalGear;

    /// <summary>An empty message (nobody waiting, away from any site) arrives as null, which means the same.</summary>
    private void OnHint(RitualHintPacket? packet)
    {
        hint = packet;
        hintAtMs = api.World.ElapsedMilliseconds;
    }

    /// <summary>
    /// The game shows the held item's help only for a moment after switching to it. When what the gear would do changes
    /// while it is in hand (walking up to a rift), show it again, so the change can be seen.
    /// </summary>
    private void Refresh()
    {
        var key = hint is { } h ? (h.State, h.AtSite, h.Count) : ((RitualState, bool, int)?)null;
        if (key == shownKey)
        {
            return;
        }

        shownKey = key;
        if (!IsGear(api.World.Player?.InventoryManager?.ActiveHotbarSlot) || !ClientSettings.ShowBlockInteractionHelp)
        {
            return;
        }

        try
        {
            if (api.Gui.LoadedGuis.OfType<HudHotbar>().FirstOrDefault() is { } hotbar)
            {
                Traverse.Create(hotbar).Field("prevIndex").SetValue(-1);
                Traverse.Create(hotbar).Method("RecomposeActiveSlotHoverText", api.World.Player!.InventoryManager.ActiveHotbarSlotNumber).GetValue();
            }
        }
        catch (Exception e)
        {
            // Only the moment the hint shows up depends on this; the hint itself is the patched help.
            api.Logger.ModVerboseDebug("Could not show the gear's help again: {0}", e.Message);
        }
    }

    // ---------- the hint ----------

    /// <summary>The gear's held help with the ritual's line in place of "use on ground". Unchanged with nobody waiting.</summary>
    public WorldInteraction[] Help(WorldInteraction[] vanilla)
    {
        if (hint is not { } h || h.State == RitualState.None)
        {
            return vanilla;
        }

        string vanillaText = Lang.Get("heldhelp-useonground");
        string text = h.State == RitualState.Ready ? Lang.Get("temporalrebirth:heldhelp-ritual", h.Count)
            : h.AtSite ? Lang.Get("temporalrebirth:heldhelp-noritual", Reason(h))
            : Lang.Get("temporalrebirth:heldhelp-useonground-why", vanillaText, Reason(h));

        // The text goes in as the lang code: the game shows a code it does not know as it is.
        var line = new WorldInteraction { ActionLangCode = text, MouseButton = EnumMouseButton.Right };
        int i = Array.FindIndex(vanilla, w => w.ActionLangCode == "heldhelp-useonground");
        WorldInteraction[] result = (WorldInteraction[])vanilla.Clone();
        if (i >= 0)
        {
            result[i] = line;
            return result;
        }

        return [line, .. vanilla];
    }

    private string Reason(RitualHintPacket h) => h.State switch
    {
        RitualState.NobodyRevivable => Lang.Get("temporalrebirth:ritual-why-nobody"),
        RitualState.Cooldown => Lang.Get("temporalrebirth:ritual-why-cooldown", TimerHud.Format(Math.Max(0, h.SecondsLeft - ((api.World.ElapsedMilliseconds - hintAtMs) / 1000)))),
        RitualState.TooFarRift => Lang.Get("temporalrebirth:ritual-why-rift"),
        RitualState.TooFarPlace => Lang.Get("temporalrebirth:ritual-why-place"),
        RitualState.TooFarPlayer => Lang.Get("temporalrebirth:ritual-why-player"),
        RitualState.CannotCast => Lang.Get("temporalrebirth:ritual-why-cannot"),
        _ => Lang.Get("temporalrebirth:ritual-why-nobody"),
    };

    // ---------- the hold ----------

    /// <returns>false to skip vanilla's start: the hold is a ritual, or nothing.</returns>
    public bool OnHoldStart(BlockSelection? blockSel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (blockSel == null || hint is not { } h || h.State == RitualState.None
            || api.World.BlockAccessor.GetBlock(blockSel.Position) is BlockStaticTranslocator)
        {
            return true;
        }

        if (h.State == RitualState.Ready)
        {
            handling = EnumHandHandling.PreventDefault;
            BeginHold(blockSel);
            return false;
        }

        if (!h.AtSite)
        {
            return true;
        }

        // Here the gear would neither bring anyone back nor set the spawn point: the hold should not even begin.
        if (firstEvent)
        {
            api.TriggerIngameError(this, "temporalrebirth-noritual", Lang.Get("temporalrebirth:ritual-blocked", Reason(h)));
        }

        return false;
    }

    private void BeginHold(BlockSelection blockSel)
    {
        EndHold();
        holdAt = blockSel.Position.ToVec3d().Add(blockSel.HitPosition);
        sound = api.World.LoadSound(new SoundParams
        {
            Location = new AssetLocation("sounds/effect/translocate-active.ogg"),
            ShouldLoop = true,
            Position = holdAt.ToVec3f(),
            DisposeOnFinish = false,
            Volume = 0.5f,
            Pitch = 0.6f,
        });
        sound?.Start();
        progress.Show();
    }

    /// <summary>Every frame of the hold: the gear spins faster, the sound rises, motes are pulled into the spot.</summary>
    public void StepHold(ItemTemporalGear gear, float secondsUsed)
    {
        if (holdAt == null)
        {
            return;
        }

        float done = Math.Clamp(secondsUsed / RitualLook.HoldSeconds, 0, 1);
        float spin = GameMath.Mod(api.World.ElapsedMilliseconds / (50f - (secondsUsed * 20f)), 360f);
        gear.FpHandTransform.Rotation.Y = spin;
        gear.TpHandTransform.Rotation.Y = spin;
        sound?.SetPitch(0.6f + (done * 0.9f));
        sound?.SetVolume(0.5f + (done * 0.5f));
        api.World.AddCameraShake(ShakePerStep * done);
        progress.Set(done);

        long now = api.World.ElapsedMilliseconds;
        if (now - lastMoteMs >= 30)
        {
            lastMoteMs = now;
            for (int i = 0; i < 1 + (int)(done * 3); i++)
            {
                api.World.SpawnParticles(RitualLook.Mote(api.World.Rand, holdAt, done));
            }
        }
    }

    public void EndHold()
    {
        holdAt = null;
        sound?.Stop();
        sound?.Dispose();
        sound = null;
        progress.Hide();
    }

    // ---------- the chooser ----------

    private void OnOpen(ChooserOpenPacket? packet)
    {
        chooser?.Dismiss();
        if (packet == null || packet.Entries.Count == 0)
        {
            return;
        }

        chooser = new RitualChooser(api, packet, pick =>
        {
            if (channel.Connected)
            {
                channel.SendPacket(pick);
            }
        });
        chooser.TryOpen();
    }

    public void Dispose()
    {
        EndHold();
        chooser?.Dismiss();
        progress.Dispose();
    }
}
