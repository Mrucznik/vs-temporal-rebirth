using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>Decides where players out of lives stay, and where they come back.</summary>
internal class Places(ICoreServerAPI api, RebirthConfig config, Afterlife afterlife, Zones zones)
{
    /// <summary>Where their time out of lives starts.</summary>
    public Vec3d StartOf(IServerPlayer player, LostPlayer lost, RebirthConfig rules)
    {
        Vec3d death = lost.DeathVec ?? player.Entity.Pos.XYZ;
        switch (rules.PlaceAt)
        {
            case Place.Spawn:
                return player.GetSpawnPosition(false).XYZ;
            case Place.Zone:
                if (StartInZone(player, death, rules) is { } inZone)
                {
                    return inZone;
                }

                break;
            case Place.Fixed:
                if (afterlife.Point("start") is { } point)
                {
                    return point;
                }

                api.Logger.Error("[temporalrebirth] No place is marked for players out of lives (/rebirth point set start); {0} stays where they died.", player.PlayerName);
                break;
        }

        // Died inside a block (suffocation, sand): a solid ghost would be stuck there.
        return rules.Form == Form.Ghost ? SafeSpot.FindAbove(api.World.BlockAccessor, death) ?? death : death;
    }

    /// <summary>The marked place if it is in the zone, else where they died if that is, else the closest spot of the zone.</summary>
    private Vec3d? StartInZone(IServerPlayer player, Vec3d death, RebirthConfig rules)
    {
        Vec3d? point = afterlife.Point("start");
        List<LandClaim> claims = zones.Claims(rules.PlaceGroup);
        if (claims.Count == 0)
        {
            zones.ReportMissing(player, rules.PlaceGroup);
            return point;
        }

        if (point != null && Zones.Contains(claims, point))
        {
            return point;
        }

        api.Logger.Error("[temporalrebirth] No place is marked inside the zone of the group {0} (/rebirth point set start).", rules.PlaceGroup);
        return Zones.Contains(claims, death) ? null : zones.Nearest(claims, point ?? death);
    }

    /// <summary>
    /// Where a walking player who has just got to <paramref name="here"/> should stand; only now the land is loaded for sure.
    /// Null when there is no safe ground.
    /// </summary>
    public Vec3d? Ground(RebirthConfig rules, Vec3d here) => rules.PlaceAt switch
    {
        Place.Body => SafeSpot.FindGround(api.World.BlockAccessor, here),
        Place.Zone when zones.Claims(rules.PlaceGroup) is { Count: > 0 } claims => zones.Ground(claims, here),
        _ => here,
    };

    /// <summary>Where they come back to life.</summary>
    public Vec3d DestinationOf(IServerPlayer player, LostPlayer lost)
    {
        Vec3d spawn = player.GetSpawnPosition(false).XYZ;
        if (lost.Return is { Reason: ReturnReason.Ritual } ritual)
        {
            Vec3d here = player.Entity.Pos.XYZ;
            IBlockAccessor blocks = api.World.BlockAccessor;
            return ritual.ToSpawn ? spawn
                : ritual.DestinationVec ?? SafeSpot.FindGround(blocks, here) ?? SafeSpot.FindGround(blocks, here, true) ?? here;
        }

        switch (lost.RulesIn(config).ReturnAt)
        {
            case Place.Body:
                return lost.DeathVec ?? spawn;
            case Place.Fixed:
                if (afterlife.Point("return") is { } point)
                {
                    return point;
                }

                api.Logger.Error("[temporalrebirth] No place is marked for coming back (/rebirth point set return); {0} goes to their spawn point.", player.PlayerName);
                return spawn;
            default:
                return spawn;
        }
    }
}
