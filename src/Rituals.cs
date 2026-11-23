using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>A normal, living player offers a temporal gear to bring back someone who is out of lives.</summary>
internal class Rituals(ICoreServerAPI api, RebirthConfig config, Afterlife afterlife, Returns returns, Messages messages)
{
    /// <summary>
    /// Caster uid -> who was chosen with /rebirth target: the uid and that time out of lives, so a choice does not outlive
    /// it. Not persisted; a restart clears choices.
    /// </summary>
    private readonly Dictionary<string, (string Uid, LostPlayer Lost)> targets = new();

    private sealed record Candidate(string Uid, LostPlayer Lost, IServerPlayer? Player);

    public void Choose(IServerPlayer caster, string uid, LostPlayer lost) => targets[caster.PlayerUID] = (uid, lost);

    public static bool CanPerform(IServerPlayer player, Afterlife afterlife) =>
        player.Entity.Alive
        && !afterlife.Contains(player.PlayerUID)
        && player.WorldData.CurrentGameMode is EnumGameMode.Survival or EnumGameMode.Creative
        && !DownedCompat.IsDowned(player.Entity);

    /// <summary>Whether a gear held here is a ritual rather than the vanilla "set spawn point".</summary>
    public bool IsRitualSite(IServerPlayer caster)
    {
        Vec3d pos = caster.Entity.Pos.XYZ;
        return config.RitualWhere switch
        {
            RitualPlace.Rift => Rifts.IsNearActiveRift(api.World, pos, config.RitualRange),
            RitualPlace.FixedPlace => afterlife.Point("ritual") is { } point && point.DistanceTo(pos) <= config.RitualRange,
            RitualPlace.NearPlayer => Nearest(caster) != null,
            // Without a chosen player who can be brought back, a gear works as usual.
            RitualPlace.Anywhere => Chosen(caster, Candidates(caster)) != null,
            _ => false,
        };
    }

    /// <summary>Returns false when nobody was brought back; the caller keeps the gear in that case.</summary>
    public bool Perform(IServerPlayer caster, BlockPos ritualPos)
    {
        Candidate? target = config.RitualWhere == RitualPlace.NearPlayer ? Nearest(caster) : PickTarget(caster);
        if (target == null)
        {
            return false;
        }

        if (config.RitualCooldownHours > 0 && afterlife.LastRitual(target.Uid) is { } last && DateTime.UtcNow < last.AddHours(config.RitualCooldownHours))
        {
            messages.Tell(caster, "RitualCooldown", ("player", target.Lost.Name), ("time", Messages.Time(last.AddHours(config.RitualCooldownHours))));
            return false;
        }

        targets.Remove(caster.PlayerUID);
        afterlife.RitualDone(target.Uid);
        returns.ByRitual(target.Uid, target.Lost, caster, BesideCaster(caster, ritualPos), config.RitualReviveAt);

        messages.Tell(caster, "RitualDone", ("player", target.Lost.Name));
        messages.TellEveryone("AnnounceRitual", ("caster", caster.PlayerName), ("player", target.Lost.Name));
        api.World.PlaySoundAt(new AssetLocation("sounds/effect/portal.ogg"), ritualPos.X + 0.5, ritualPos.Y + 0.5, ritualPos.Z + 0.5, null, false, 48f);
        api.Logger.Audit("[temporalrebirth] {0} revived {1} with a ritual near {2}", caster.PlayerName, target.Lost.Name, ritualPos);
        return true;
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

    private Candidate? Nearest(IServerPlayer caster) =>
        Candidates(caster)
            .Where(c => c.Player != null && c.Player.Entity.Pos.DistanceTo(caster.Entity.Pos) <= config.RitualRange)
            .MinBy(c => c.Player!.Entity.Pos.DistanceTo(caster.Entity.Pos));

    private Candidate? Chosen(IServerPlayer caster, List<Candidate> candidates) =>
        targets.TryGetValue(caster.PlayerUID, out var chosen) ? candidates.FirstOrDefault(c => c.Uid == chosen.Uid && c.Lost == chosen.Lost) : null;

    private Candidate? PickTarget(IServerPlayer caster)
    {
        List<Candidate> candidates = Candidates(caster);
        Candidate? target = Chosen(caster, candidates);
        if (config.RitualWhere != RitualPlace.Anywhere && candidates.Count == 1)
        {
            target ??= candidates[0];
        }

        if (target == null && candidates.Count == 0)
        {
            messages.Tell(caster, "RitualNobody");
        }
        else if (target == null)
        {
            messages.Tell(caster, "RitualChoose", ("players", string.Join(", ", candidates.Select(c => c.Lost.Name))));
        }

        return target;
    }
}
