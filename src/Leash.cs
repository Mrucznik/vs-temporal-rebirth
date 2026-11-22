using Vintagestory.API.MathTools;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>
/// Server side of keeping players out of lives in their place. Their own client stops them at the edge (see LeashGuard);
/// here only those who got far outside anyway are brought back.
/// </summary>
internal class Leash(ICoreServerAPI api, RebirthConfig config, Zones zones, Messages messages)
{
    private readonly Dictionary<string, long> lastMessageMs = new();

    /// <summary>The last position of each player inside their limit.</summary>
    private readonly Dictionary<string, Vec3d> lastInside = new();

    public void Forget(string uid) => lastInside.Remove(uid);

    /// <summary>Called every second, and when a player arrives. False when they have to travel back to <see cref="LostPlayer.Start"/>.</summary>
    public bool Enforce(IServerPlayer player, LostPlayer lost, RebirthConfig rules)
    {
        LeashShape? shape = ShapeFor(player, lost, rules);
        Tell(player.Entity, shape, rules);
        if (shape == null || !player.Entity.Alive)
        {
            return true;
        }

        Vec3d pos = player.Entity.Pos.XYZ;
        double outside = shape.DistanceOutside(pos);
        if (outside == 0)
        {
            lastInside[player.PlayerUID] = pos;
        }

        if (outside <= LeashShape.Slack)
        {
            return true;
        }

        player.Entity.TryUnmount();
        TellTooFar(player);
        return PullBack(player, lost, rules, shape, pos);
    }

    private LeashShape? ShapeFor(IServerPlayer player, LostPlayer lost, RebirthConfig rules)
    {
        if (rules.PlaceAt != Place.Zone)
        {
            return rules.Radius > 0 && lost.StartVec is { } center ? new LeashSphere(center, rules.Radius) : null;
        }

        List<LandClaim> claims = zones.Claims(rules.PlaceGroup);
        if (claims.Count > 0)
        {
            return new LeashZone(claims);
        }

        zones.ReportMissing(player, rules.PlaceGroup);
        return null;
    }

    private void Tell(EntityPlayer entity, LeashShape? shape, RebirthConfig rules)
    {
        ClientState.Set(entity, ClientState.Leash, shape is LeashZone ? LeashZone.Name : shape?.ToString());
        ClientState.Set(entity, ClientState.TooFarText, shape == null ? null : config.Message("TooFar"));
        int? group = rules.PlaceAt == Place.Zone ? zones.GroupId(rules.PlaceGroup) : null;
        ClientState.Set(entity, ClientState.ZoneGroup, group);
        ClientState.Set(entity, ClientState.ZoneAccess, group != null && rules.Walks ? rules.PlaceAccess.ToString() : null);
    }

    private bool PullBack(IServerPlayer player, LostPlayer lost, RebirthConfig rules, LeashShape shape, Vec3d pos)
    {
        bool walks = rules.Walks;
        if ((walks || shape is LeashZone) && lastInside.TryGetValue(player.PlayerUID, out Vec3d? inside) && shape.DistanceOutside(inside) == 0)
        {
            player.Entity.TeleportToDouble(inside.X, inside.Y, inside.Z);
            return true;
        }

        if (shape is LeashSphere sphere)
        {
            Vec3d target = walks ? sphere.Center : Edge(player, sphere, pos);
            player.Entity.TeleportToDouble(target.X, target.Y, target.Z);
            return true;
        }

        // After a restart, or when the claims changed. May be far away, so travel like at the start of the sentence.
        Vec3d spot = lost.StartVec is { } start && shape.DistanceOutside(start) == 0 ? start : zones.Nearest(zones.Claims(rules.PlaceGroup), pos);
        lost.Start = LostPlayer.Pack(spot);
        return false;
    }

    /// <summary>Ghosts end on the edge, where they were heading. From very far away (e.g. an admin /tp) they snap to the middle.</summary>
    private Vec3d Edge(IServerPlayer player, LeashSphere sphere, Vec3d pos)
    {
        Vec3d center = sphere.Center;
        double dist = pos.DistanceTo(center);
        double reach = Math.Max(0, sphere.Radius - 1);
        Vec3d target = dist > sphere.Radius * 4
            ? center
            : center.AddCopy((pos.X - center.X) / dist * reach, (pos.Y - center.Y) / dist * reach, (pos.Z - center.Z) / dist * reach);
        if (!player.WorldData.NoClip && !SafeSpot.IsFree(api.World.BlockAccessor, target))
        {
            // The line can cross a hill; a solid ghost must not land inside it.
            // Nor on top of a hill that sticks out of the sphere.
            Vec3d? above = SafeSpot.FindAbove(api.World.BlockAccessor, target);
            target = above != null && sphere.DistanceOutside(above) == 0 ? above : SafeSpot.FindAbove(api.World.BlockAccessor, center) ?? center;
        }

        return target;
    }

    private void TellTooFar(IServerPlayer player)
    {
        long now = api.World.ElapsedMilliseconds;
        if (!lastMessageMs.TryGetValue(player.PlayerUID, out long last) || now - last > 15_000)
        {
            lastMessageMs[player.PlayerUID] = now;
            messages.Tell(player, "TooFar");
        }
    }
}
