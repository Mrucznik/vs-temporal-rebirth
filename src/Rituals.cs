using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>A normal, living player offers a temporal gear to bring back someone who is out of lives.</summary>
internal class Rituals(ICoreServerAPI api, RebirthConfig config, Afterlife afterlife, Rebirth rebirth, Messages messages)
{
    /// <summary>Caster uid -> uid chosen with /rebirth target. Not persisted; a restart clears choices.</summary>
    private readonly Dictionary<string, string> targets = new();

    private sealed record Candidate(string Uid, LostPlayer Lost, IServerPlayer? Player);

    public void Choose(IServerPlayer caster, string uid) => targets[caster.PlayerUID] = uid;

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
            // Without a chosen player a gear anywhere works as usual.
            RitualPlace.Anywhere => targets.ContainsKey(caster.PlayerUID),
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
        rebirth.ReturnByRitual(target.Uid, target.Lost, caster.PlayerName, Destination(caster, target));

        messages.Tell(caster, "RitualDone", ("player", target.Lost.Name));
        messages.TellEveryone("AnnounceRitual", ("caster", caster.PlayerName), ("player", target.Lost.Name));
        api.World.PlaySoundAt(new AssetLocation("sounds/effect/portal.ogg"), ritualPos.X + 0.5, ritualPos.Y + 0.5, ritualPos.Z + 0.5, null, false, 48f);
        api.Logger.Audit("[temporalrebirth] {0} revived {1} with a ritual near {2}", caster.PlayerName, target.Lost.Name, ritualPos);
        return true;
    }

    /// <summary>Players a ritual can bring back now: online ones, and banned ones if it lifts bans.</summary>
    private List<Candidate> Candidates(IServerPlayer caster)
    {
        var found = new List<Candidate>();
        foreach (var (uid, lost) in afterlife.Players.OrderBy(p => p.Value.DiedAtUtc))
        {
            if (lost.Phase != Phase.Serving || uid == caster.PlayerUID)
            {
                continue;
            }

            if (rebirth.FormOf(lost) == Form.Ban)
            {
                if (config.RitualLiftsBan)
                {
                    found.Add(new Candidate(uid, lost, null));
                }
            }
            else if (api.World.PlayerByUid(uid) is IServerPlayer { Entity: not null, ConnectionState: EnumClientState.Playing } player)
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

    private Candidate? PickTarget(IServerPlayer caster)
    {
        List<Candidate> candidates = Candidates(caster);
        Candidate? target = targets.TryGetValue(caster.PlayerUID, out string? uid) ? candidates.FirstOrDefault(c => c.Uid == uid) : null;
        target ??= candidates.Count == 1 ? candidates[0] : null;
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

    private Vec3d? Destination(IServerPlayer caster, Candidate target)
    {
        switch (config.RitualReviveAt)
        {
            case ReviveAt.Caster:
                return caster.Entity.Pos.XYZ;
            case ReviveAt.Body when target.Lost.DeathVec != null:
                return target.Lost.DeathVec;
            case ReviveAt.InPlace:
                return null;
            default:
                // A banned player's own spawn point is only known once they join.
                return (target.Player ?? api.World.PlayerByUid(target.Uid) as IServerPlayer)?.GetSpawnPosition(false).XYZ;
        }
    }
}
