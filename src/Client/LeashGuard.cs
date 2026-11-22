using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TemporalRebirth;

/// <summary>Client side: stops the player at the edge of where they may be, smoothly and without waiting for the server.</summary>
internal class LeashGuard(ICoreClientAPI api)
{
    private const long HitIntervalMs = 600, MessageIntervalMs = 15_000;

    /// <summary>Claims change rarely and there can be many.</summary>
    private const long ZoneRefreshMs = 1000;

    private readonly CollisionTester collisions = new();
    private string? known;
    private long knownAtMs, lastHitMs = -HitIntervalMs, lastMessageMs = -MessageIntervalMs;

    /// <summary>The Harmony patch is static; it reaches this through here.</summary>
    internal static LeashGuard? Current { get; private set; }

    /// <summary>What limits the player now.</summary>
    public LeashShape? Shape { get; private set; }

    /// <summary>The player ran into the edge, at this position.</summary>
    public event Action<Vec3d>? Hit;

    public void Start()
    {
        Current = this;
        api.Event.LeaveWorld += () => Current = null;
        api.Event.RegisterGameTickListener(_ => Refresh(), 100);
    }

    private void Refresh()
    {
        if (api.World.Player?.Entity?.WatchedAttributes is not { } attrs)
        {
            return;
        }

        string? text = attrs.GetString(ClientState.Leash);
        bool zone = text == LeashZone.Name;
        long now = api.World.ElapsedMilliseconds;
        if (text == known && !(zone && now - knownAtMs > ZoneRefreshMs))
        {
            return;
        }

        known = text;
        knownAtMs = now;
        Shape = text == null ? null : zone ? ZoneShape(attrs.GetInt(ClientState.ZoneGroup, -1)) : LeashSphere.Parse(text);
    }

    private LeashZone? ZoneShape(int group)
    {
        List<LandClaim> claims = api.World.Claims.All.Where(c => Zones.IsPart(c, group)).ToList();
        return claims.Count > 0 ? new LeashZone(claims) : null;
    }

    /// <summary>After each step of the player's own movement, before it is sent to the server.</summary>
    public void AfterStep(EntityPlayer entity, EntityPos pos, Vec3d from)
    {
        if (Shape is not { } shape || entity != api.World.Player?.Entity || entity.World.Side != EnumAppSide.Client || !entity.Alive || entity.MountedOn != null)
        {
            return;
        }

        // Places are in the coordinates of the server, where dimensions are stacked.
        double dimension = pos.InternalY - pos.Y;
        Vec3d to = pos.XYZ;
        Vec3d start = from.AddCopy(0, dimension, 0);
        if (shape.DistanceOutside(start) > LeashShape.Slack || shape.DistanceOutside(to) == 0)
        {
            return;
        }

        IPlayer player = api.World.Player;
        Vec3d stopped = shape.Stop(start, to, !player.WorldData.FreeMove);
        if (stopped == to)
        {
            return;
        }

        // Sliding along the edge must not end inside a hill.
        if (!player.WorldData.NoClip && collisions.IsColliding(api.World.BlockAccessor, entity.CollisionBox, stopped, false))
        {
            stopped = start;
        }

        Vec3d outward = to - stopped;
        double length = outward.Length();
        if (length < 1e-6)
        {
            return;
        }

        pos.SetPos(stopped.X, stopped.Y - dimension, stopped.Z);
        outward.Mul(1 / length);
        double speed = pos.Motion.Dot(outward);
        if (speed > 0)
        {
            pos.Motion.Add(outward.X * -speed, outward.Y * -speed, outward.Z * -speed);
        }

        Report(entity, stopped.AddCopy(0, entity.LocalEyePos.Y * 0.6, 0));
    }

    private void Report(EntityPlayer entity, Vec3d at)
    {
        long now = api.World.ElapsedMilliseconds;
        if (now - lastHitMs >= HitIntervalMs)
        {
            lastHitMs = now;
            Hit?.Invoke(at);
        }

        if (now - lastMessageMs >= MessageIntervalMs)
        {
            lastMessageMs = now;
            api.ShowChatMessage(entity.WatchedAttributes.GetString(ClientState.TooFarText) ?? Lang.Get("temporalrebirth:msg-toofar"));
        }
    }
}

/// <summary>Cosmetic for honest players: without it the server still pulls back whoever gets far outside.</summary>
[HarmonyPatch(typeof(EntityBehaviorPlayerPhysics), nameof(EntityBehaviorPlayerPhysics.SimPhysics))]
internal static class LeashStepPatch
{
    private static void Prefix(EntityPos pos, out Vec3d __state) => __state = new Vec3d(pos.X, pos.Y, pos.Z);

    private static void Postfix(EntityBehaviorPlayerPhysics __instance, EntityPos pos, Vec3d __state)
    {
        if (__instance.entity is EntityPlayer player)
        {
            LeashGuard.Current?.AfterStep(player, pos, __state);
        }
    }
}
