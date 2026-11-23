using TemporalRebirth.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalRebirth;

/// <summary>A normal, living player offers a temporal gear to bring back someone who is out of lives.</summary>
internal class Rituals(ICoreServerAPI api, RebirthConfig config, Afterlife afterlife, Returns returns, Messages messages)
{
    /// <summary>
    /// Caster uid -> who was chosen with /rebirth target: the uid and that time out of lives, so a choice does not outlive
    /// it. Not persisted; a restart clears choices.
    /// </summary>
    private readonly Dictionary<string, (string Uid, LostPlayer Lost)> targets = new();

    /// <summary>Caster uid -> the chooser that caster has open. The gear is only taken when a choice comes back.</summary>
    private readonly Dictionary<string, Pending> pending = new();

    /// <summary>Caster uid -> the last hint sent and when, so an unchanged one is not sent again.</summary>
    private readonly Dictionary<string, (RitualHintPacket Hint, long SentMs)> hints = new();

    /// <summary>Caster uid -> a hold that is a ritual: where it is aimed and when particles were last sent for it.</summary>
    private readonly Dictionary<string, (Vec3d At, long LastMs)> holds = new();

    private IServerNetworkChannel? channel;

    private sealed record Candidate(string Uid, LostPlayer Lost, IServerPlayer? Player);

    /// <summary>What was offered: the same time out of lives, not just the same player, must still be waiting.</summary>
    private sealed record Pending(BlockPos Pos, Vec3d From, long UntilMs, List<(string Uid, LostPlayer Lost)> Offered, bool AllowBind);

    /// <summary>Hints only matter to whoever holds a gear; a second is quick enough for walking up to a rift.</summary>
    private const int HintIntervalMs = 1000;
    private const int ParticleIntervalMs = 150;

    public void Register(IServerNetworkChannel net)
    {
        channel = net;
        net.SetMessageHandler<ChooserPickPacket>(OnPick);
        api.Event.RegisterGameTickListener(_ => SendHints(), HintIntervalMs);
        api.Event.RegisterGameTickListener(_ => CheckPending(), 500);
        // Switching to a gear shows its hint at once, not up to a second later with whatever it said last time.
        api.Event.AfterActiveSlotChanged += (player, _) => SendHint(player);
        api.Event.PlayerDisconnect += player =>
        {
            pending.Remove(player.PlayerUID);
            hints.Remove(player.PlayerUID);
            holds.Remove(player.PlayerUID);
        };
    }

    public void Choose(IServerPlayer caster, string uid, LostPlayer lost) => targets[caster.PlayerUID] = (uid, lost);

    public static bool CanPerform(IServerPlayer player, Afterlife afterlife) =>
        player.Entity.Alive
        && !afterlife.Contains(player.PlayerUID)
        && player.WorldData.CurrentGameMode is EnumGameMode.Survival or EnumGameMode.Creative
        && !DownedCompat.IsDowned(player.Entity);

    /// <summary>Whether a gear held here is a ritual rather than the vanilla "set spawn point".</summary>
    public bool IsRitualSite(IServerPlayer caster) => IsSite(caster, Candidates(caster));

    private bool IsSite(IServerPlayer caster, List<Candidate> candidates)
    {
        Vec3d pos = caster.Entity.Pos.XYZ;
        return config.RitualWhere switch
        {
            RitualPlace.Rift => Rifts.IsNearActiveRift(api.World, pos, config.RitualRange),
            RitualPlace.FixedPlace => afterlife.Point("ritual") is { } point && point.DistanceTo(pos) <= config.RitualRange,
            RitualPlace.NearPlayer => Near(caster, candidates).Count > 0,
            // The gear opens the chooser, which can still bind the caster here. With nobody to choose it works as usual.
            RitualPlace.Anywhere => candidates.Any(c => CooldownEnd(c) == null),
            _ => false,
        };
    }

    // ---------- the hint ----------

    /// <summary>What holding a gear would do for this caster now, for the client.</summary>
    public RitualHintPacket Hint(IServerPlayer caster)
    {
        if (LifeRules.UnlimitedLives(api.World))
        {
            return new RitualHintPacket { State = RitualState.None };
        }

        if (afterlife.Contains(caster.PlayerUID))
        {
            // Out of lives a gear does nothing anywhere: it would be a way out.
            return new RitualHintPacket { State = RitualState.CannotCast, AtSite = true };
        }

        List<Candidate> candidates = Candidates(caster);
        bool site = IsSite(caster, candidates);
        bool waiting = afterlife.Players.Any(p => p.Key != caster.PlayerUID && p.Value.Phase != Phase.Returning);
        if (config.RitualWhere == RitualPlace.Disabled || !waiting)
        {
            return new RitualHintPacket { State = RitualState.None, AtSite = site };
        }

        if (!CanPerform(caster, afterlife))
        {
            return new RitualHintPacket { State = RitualState.CannotCast, AtSite = site };
        }

        if (candidates.Count == 0)
        {
            return new RitualHintPacket { State = RitualState.NobodyRevivable, AtSite = site };
        }

        if (!site && config.RitualWhere != RitualPlace.Anywhere)
        {
            RitualState far = config.RitualWhere switch
            {
                RitualPlace.Rift => RitualState.TooFarRift,
                RitualPlace.FixedPlace => RitualState.TooFarPlace,
                _ => RitualState.TooFarPlayer,
            };
            return new RitualHintPacket { State = far };
        }

        List<Candidate> pool = Pool(caster, candidates);
        int ready = pool.Count(c => CooldownEnd(c) == null);
        if (ready > 0)
        {
            return new RitualHintPacket { State = RitualState.Ready, AtSite = true, Count = ready };
        }

        DateTime first = pool.Select(c => CooldownEnd(c)!.Value).Min();
        return new RitualHintPacket { State = RitualState.Cooldown, AtSite = site, SecondsLeft = (int)Math.Ceiling((first - DateTime.UtcNow).TotalSeconds) };
    }

    private void SendHints()
    {
        foreach (IServerPlayer player in api.World.AllOnlinePlayers.Cast<IServerPlayer>())
        {
            SendHint(player);
        }
    }

    private void SendHint(IServerPlayer player)
    {
        // Only whoever holds a gear sees a hint; working it out for everyone would be wasted.
        if (channel == null || player.ConnectionState != EnumClientState.Playing || player.Entity == null
            || player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible is not ItemTemporalGear)
        {
            return;
        }

        RitualHintPacket hint = Hint(player);
        long now = api.World.ElapsedMilliseconds;
        // The client counts a cooldown down itself; only a jump (another ritual, a changed setting) is sent.
        if (hints.TryGetValue(player.PlayerUID, out var last) && hint.SameAs(last.Hint)
            && Math.Abs(last.Hint.SecondsLeft - ((now - last.SentMs) / 1000) - hint.SecondsLeft) <= 2)
        {
            return;
        }

        hints[player.PlayerUID] = (hint, now);
        channel.SendPacket(hint, player);
    }

    // ---------- the hold ----------

    /// <summary>The hold has begun; true when it is a ritual that can happen, so vanilla's start is skipped.</summary>
    public bool BeginHold(IServerPlayer caster, BlockSelection blockSel)
    {
        holds.Remove(caster.PlayerUID);
        if (Hint(caster).State != RitualState.Ready)
        {
            return false;
        }

        holds[caster.PlayerUID] = (blockSel.Position.ToVec3d().Add(blockSel.HitPosition), 0);
        return true;
    }

    /// <summary>Lets players near the caster see the ritual too. The caster draws their own, every frame.</summary>
    public void StepHold(IServerPlayer caster, float secondsUsed)
    {
        if (!holds.TryGetValue(caster.PlayerUID, out var hold))
        {
            return;
        }

        long now = api.World.ElapsedMilliseconds;
        if (now - hold.LastMs < ParticleIntervalMs)
        {
            return;
        }

        holds[caster.PlayerUID] = (hold.At, now);
        float progress = Math.Clamp(secondsUsed / RitualLook.HoldSeconds, 0, 1);
        for (int i = 0; i < 2; i++)
        {
            api.World.SpawnParticles(RitualLook.Mote(api.World.Rand, hold.At, progress), caster);
        }
    }

    public void EndHold(IServerPlayer caster) => holds.Remove(caster.PlayerUID);

    /// <summary>
    /// The gear was held long enough at a ritual site. Brings someone back at once when it is clear whom, otherwise opens
    /// the chooser. The gear is taken only together with a revival.
    /// </summary>
    public void Complete(IServerPlayer caster, ItemSlot slot, BlockPos ritualPos)
    {
        EndHold(caster);
        pending.Remove(caster.PlayerUID);
        List<Candidate> pool = Pool(caster, Candidates(caster));
        if (pool.Count == 0)
        {
            messages.Tell(caster, "RitualNobody");
            return;
        }

        List<Candidate> ready = pool.Where(c => CooldownEnd(c) == null).ToList();
        if (ready.Count == 0)
        {
            Candidate first = pool.MinBy(c => CooldownEnd(c)!.Value)!;
            messages.Tell(caster, "RitualCooldown", ("player", first.Lost.Name), ("time", Messages.Time(CooldownEnd(first)!.Value)));
            return;
        }

        Candidate? target = Chosen(caster, ready);
        // Next to players the nearest is the one meant: walking up to someone is the choice.
        if (target == null && config.RitualWhere == RitualPlace.NearPlayer)
        {
            target = ready[0];
        }

        // Anywhere a gear may also bind the caster to the place, so even one candidate needs the chooser, unless the world
        // forbids that.
        if (target == null && ready.Count == 1 && !CanBind())
        {
            target = ready[0];
        }

        if (target != null)
        {
            if (Perform(caster, target, ritualPos))
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }

            return;
        }

        OpenChooser(caster, ready, ritualPos);
    }

    // ---------- the chooser ----------

    private void OpenChooser(IServerPlayer caster, List<Candidate> ready, BlockPos ritualPos)
    {
        Vec3d from = caster.Entity.Pos.XYZ;
        bool allowBind = CanBind();
        pending[caster.PlayerUID] = new Pending(ritualPos.Copy(), from, api.World.ElapsedMilliseconds + (RitualNet.ChooserSeconds * 1000L),
            ready.Select(c => (c.Uid, c.Lost)).ToList(), allowBind);

        var packet = new ChooserOpenPacket { AllowBind = allowBind, X = from.X, Y = from.Y, Z = from.Z };
        foreach (Candidate c in ready)
        {
            DateTime? end = c.Lost.EndUtc(config);
            packet.Entries.Add(new ChooserEntry
            {
                Uid = c.Uid,
                Name = c.Lost.Name,
                Form = (int)c.Lost.RulesIn(config).Form,
                SecondsLeft = end is { } at ? Math.Max(0, (int)Math.Ceiling((at - DateTime.UtcNow).TotalSeconds)) : -1,
                Online = api.World.PlayerByUid(c.Uid) is IServerPlayer { ConnectionState: EnumClientState.Playing },
            });
        }

        channel?.SendPacket(packet, caster);
        api.Logger.Notification("[temporalrebirth] {0} chooses whom to revive: {1}", caster.PlayerName, string.Join(", ", ready.Select(c => c.Lost.Name)));
    }

    /// <summary>Whether the chooser also offers what the gear does without a ritual: setting the spawn point here.</summary>
    private bool CanBind() =>
        config.RitualWhere == RitualPlace.Anywhere && api.World.Config.GetAsInt("temporalGearRespawnUses", -1) != 0;

    private void OnPick(IServerPlayer caster, ChooserPickPacket? pick)
    {
        // An empty message arrives as null.
        if (pick == null || pick.Cancel)
        {
            Cancel(caster);
        }
        else
        {
            Pick(caster, pick.Bind ? null : pick.Uid);
        }
    }

    /// <summary>The chooser was closed without a choice; the gear stays.</summary>
    public void Cancel(IServerPlayer caster) => pending.Remove(caster.PlayerUID);

    /// <summary>
    /// A choice from the chooser: whom to bring back, or null to bind the caster to the place instead. Everything is
    /// checked again, since time passed and the packet comes from the client. One choice per chooser: the session is gone
    /// before anything else happens, so a second packet finds nothing.
    /// </summary>
    /// <returns>Whether the gear was used.</returns>
    public bool Pick(IServerPlayer caster, string? uid)
    {
        if (!pending.Remove(caster.PlayerUID, out Pending? session))
        {
            return false;
        }

        if (Broken(caster, session) is { } why)
        {
            messages.TellText(caster, why);
            return false;
        }

        if (FindGear(caster) is not { } slot)
        {
            messages.TellText(caster, "ritual-nogear");
            return false;
        }

        if (uid == null)
        {
            return session.AllowBind && TemporalGearPatch.BindHere(caster, slot, session.Pos);
        }

        var offered = session.Offered.FirstOrDefault(o => o.Uid == uid);
        if (offered.Uid == null)
        {
            return false;
        }

        List<Candidate> candidates = Candidates(caster);
        Candidate? target = Pool(caster, candidates).FirstOrDefault(c => c.Uid == uid && c.Lost == offered.Lost && CooldownEnd(c) == null);
        if (target == null)
        {
            messages.TellText(caster, "ritual-gone", offered.Lost.Name);
            return false;
        }

        if (!IsSite(caster, candidates))
        {
            messages.TellText(caster, "ritual-nothere");
            return false;
        }

        if (!Perform(caster, target, session.Pos))
        {
            return false;
        }

        slot.TakeOut(1);
        slot.MarkDirty();
        return true;
    }

    /// <summary>Closes choosers of casters who walked off, died or waited too long, and tells them.</summary>
    private void CheckPending()
    {
        foreach (var (uid, session) in pending.ToList())
        {
            if (api.World.PlayerByUid(uid) is not IServerPlayer caster || caster.ConnectionState != EnumClientState.Playing)
            {
                pending.Remove(uid);
            }
            else if (Broken(caster, session) is { } why)
            {
                pending.Remove(uid);
                channel?.SendPacket(new ChooserClosePacket { Unused = true }, caster);
                messages.TellText(caster, why);
            }
        }
    }

    /// <summary>Why a chooser no longer counts, as a lang key, or null while it does.</summary>
    private string? Broken(IServerPlayer caster, Pending session)
    {
        if (api.World.ElapsedMilliseconds > session.UntilMs)
        {
            return "ritual-expired";
        }

        if (caster.Entity == null || !CanPerform(caster, afterlife))
        {
            return "ritual-cannot";
        }

        return caster.Entity.Pos.XYZ.DistanceTo(session.From) > RitualNet.ChooserMoveLimit ? "ritual-moved" : null;
    }

    /// <summary>Names offered by the chooser this caster has open, none without one. For tests.</summary>
    public string[] Offered(string casterUid) =>
        pending.TryGetValue(casterUid, out Pending? session) ? session.Offered.Select(o => o.Lost.Name).ToArray() : [];

    /// <summary>The gear still in hand, else any other in the hotbar or the backpack: the active slot may change meanwhile.</summary>
    private static ItemSlot? FindGear(IServerPlayer caster)
    {
        ItemSlot? active = caster.InventoryManager.ActiveHotbarSlot;
        if (active?.Itemstack?.Collectible is ItemTemporalGear)
        {
            return active;
        }

        foreach (string name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            if (caster.InventoryManager.GetOwnInventory(name) is { } inventory)
            {
                foreach (ItemSlot slot in inventory)
                {
                    if (slot.Itemstack?.Collectible is ItemTemporalGear)
                    {
                        return slot;
                    }
                }
            }
        }

        return null;
    }

    // ---------- the revival ----------

    /// <summary>Returns false when nobody was brought back; the caller keeps the gear in that case.</summary>
    private bool Perform(IServerPlayer caster, Candidate target, BlockPos ritualPos)
    {
        if (CooldownEnd(target) is { } end)
        {
            messages.Tell(caster, "RitualCooldown", ("player", target.Lost.Name), ("time", Messages.Time(end)));
            return false;
        }

        targets.Remove(caster.PlayerUID);
        afterlife.RitualDone(target.Uid);
        returns.ByRitual(target.Uid, target.Lost, caster, BesideCaster(caster, ritualPos), config.RitualReviveAt);

        messages.Tell(caster, "RitualDone", ("player", target.Lost.Name));
        messages.TellEveryone("AnnounceRitual", ("caster", caster.PlayerName), ("player", target.Lost.Name));
        var center = new Vec3d(ritualPos.X + 0.5, ritualPos.Y + 1, ritualPos.Z + 0.5);
        api.World.PlaySoundAt(new AssetLocation("sounds/effect/portal.ogg"), center.X, center.Y, center.Z, null, false, 48f);
        api.World.SpawnParticles(RitualLook.Burst(center));
        api.Logger.Audit("[temporalrebirth] {0} revived {1} with a ritual near {2}", caster.PlayerName, target.Lost.Name, ritualPos);
        return true;
    }

    private DateTime? CooldownEnd(Candidate c)
    {
        if (config.RitualCooldownHours <= 0 || afterlife.LastRitual(c.Uid) is not { } last)
        {
            return null;
        }

        DateTime end = last.AddHours(config.RitualCooldownHours);
        return DateTime.UtcNow < end ? end : null;
    }

    private const double ReviveAside = 2;

    /// <summary>
    /// Where a player revived "at the caster" stands up: two blocks from the caster towards the block the gear was used on,
    /// on free ground. Right at the caster's feet the two would be stuck in each other, the camera inside the revived one.
    /// Falls back to the caster's own spot when there is no free ground around.
    /// </summary>
    private Vec3d BesideCaster(IServerPlayer caster, BlockPos aimed)
    {
        Vec3d at = caster.Entity.Pos.XYZ;
        double dx = aimed.X + 0.5 - at.X, dz = aimed.Z + 0.5 - at.Z;
        // The gear used on the block underfoot gives no direction: start from where the caster looks.
        double first = (dx * dx) + (dz * dz) < 0.25 ? caster.Entity.Pos.Yaw : Math.Atan2(dx, dz);
        // Towards the aimed block first, then turning round until there is free ground two blocks away.
        for (int turn = 0; turn < 8; turn++)
        {
            double angle = first + (Math.PI / 4 * ((turn + 1) / 2) * (turn % 2 == 0 ? 1 : -1));
            var aside = new Vec3d(at.X + (Math.Sin(angle) * ReviveAside), at.Y, at.Z + (Math.Cos(angle) * ReviveAside));
            if (SafeSpot.FindGround(api.World.BlockAccessor, aside) is { } ground && Math.Abs(ground.Y - at.Y) <= 3)
            {
                return ground;
            }
        }

        return at;
    }

    // ---------- who ----------

    /// <summary>Players a ritual can bring back now: online ones, and banned ones if it lifts bans.</summary>
    private List<Candidate> Candidates(IServerPlayer caster)
    {
        var found = new List<Candidate>();
        foreach (var (uid, lost) in afterlife.Players.OrderBy(p => p.Value.DiedAtUtc))
        {
            if (lost.Phase == Phase.Returning || uid == caster.PlayerUID)
            {
                continue;
            }

            if (lost.RulesIn(config).Form == Form.Ban)
            {
                // Also those who left before they were banned: they cannot join until then.
                if (config.RitualLiftsBan)
                {
                    found.Add(new Candidate(uid, lost, null));
                }
            }
            else if (lost.Phase == Phase.Serving && api.World.PlayerByUid(uid) is IServerPlayer { Entity: not null, ConnectionState: EnumClientState.Playing } player)
            {
                found.Add(new Candidate(uid, lost, player));
            }
        }

        return found;
    }

    /// <summary>Whom a ritual here can reach: next to players only those in range, nearest first; elsewhere everyone.</summary>
    private List<Candidate> Pool(IServerPlayer caster, List<Candidate> candidates) =>
        config.RitualWhere == RitualPlace.NearPlayer ? Near(caster, candidates) : candidates;

    private List<Candidate> Near(IServerPlayer caster, List<Candidate> candidates) =>
        candidates
            .Where(c => c.Player != null && c.Player.Entity.Pos.DistanceTo(caster.Entity.Pos) <= config.RitualRange)
            .OrderBy(c => c.Player!.Entity.Pos.DistanceTo(caster.Entity.Pos))
            .ToList();

    private Candidate? Chosen(IServerPlayer caster, List<Candidate> candidates) =>
        targets.TryGetValue(caster.PlayerUID, out var chosen) ? candidates.FirstOrDefault(c => c.Uid == chosen.Uid && c.Lost == chosen.Lost) : null;
}
