using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TemporalRebirth;

/// <summary>Client side: stops the player at the edge of where they may be, smoothly and without waiting for the server.</summary>
internal class LeashGuard(ICoreClientAPI api)
{
    private const long HitIntervalMs = 600;

    /// <summary>
    /// How far inside the drawn wall a sphere holds the player: the camera sits a little in front of the eyes and the near
    /// plane in front of that, so right at the wall the view slipped past it.
    /// </summary>
    internal const double SphereMargin = 0.6;

    /// <summary>Claims change rarely and there can be many.</summary>
    private const long ZoneRefreshMs = 1000;

    private readonly CollisionTester collisions = new();
    private readonly Vec3d to = new(), start = new();
    private string? known;
    private long knownAtMs, lastHitMs = -HitIntervalMs;

    /// <summary>What limits the player now, as drawn.</summary>
    public LeashShape? Shape { get; private set; }

    /// <summary>Where the player is stopped: a sphere a little smaller than the drawn one (<see cref="SphereMargin"/>).</summary>
    private LeashShape? held;

    /// <summary>The player ran into the edge, at this position.</summary>
    public event Action<Vec3d>? Hit;

    public void Start()
    {
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
        held = Shape is LeashSphere sphere ? new LeashSphere(sphere.Center, Math.Max(0.5, sphere.Radius - SphereMargin)) : Shape;
    }

    private LeashZone? ZoneShape(int group)
    {
        List<LandClaim> claims = api.World.Claims.All.Where(c => Zones.IsPart(c, group)).ToList();
        return claims.Count > 0 ? new LeashZone(claims) : null;
    }

    /// <summary>After each step of the player's own movement, before it is sent to the server.</summary>
    public void AfterStep(EntityPlayer entity, EntityPos pos, Vec3d from)
    {
        // Runs 60 times a second; everything below is only for players near the edge.
        if (held is not { } shape || entity != api.World.Player?.Entity || entity.World.Side != EnumAppSide.Client || !entity.Alive || entity.MountedOn != null)
        {
            return;
        }

        // Places are in the coordinates of the server, where dimensions are stacked.
        double dimension = pos.InternalY - pos.Y;
        // Hold whichever of the feet and the eyes is further out: a flying ghost held only by its feet put its camera
        // (and so its view) past the barrier, above or beside it.
        double eyes = entity.LocalEyePos.Y;
        to.Set(pos.X, pos.InternalY + eyes, pos.Z);
        double lift = eyes;
        if (shape.DistanceOutside(new Vec3d(pos.X, pos.InternalY, pos.Z)) > shape.DistanceOutside(to))
        {
            to.Y -= eyes;
            lift = 0;
        }

        if (shape.DistanceOutside(to) == 0)
        {
            return;
        }

        start.Set(from.X, from.Y + dimension + lift, from.Z);
        if (shape.DistanceOutside(start) > LeashShape.Slack)
        {
            return;
        }

        IPlayer player = api.World.Player;
        bool walks = !player.WorldData.FreeMove, solid = !player.WorldData.NoClip;
        double limit = Math.Max(0, shape.DistanceOutside(start));
        Vec3d? stopped = null;
        foreach (Vec3d candidate in Candidates(shape, start, to, walks))
        {
            // Every candidate is a fresh vector: the shapes may hand back their arguments.
            Vec3d feet = new(candidate.X, candidate.Y - lift, candidate.Z);
            if (!IsFinite(feet) || shape.DistanceOutside(candidate) > limit + 1e-9)
            {
                continue;
            }

            // Sliding along the edge must not end inside a hill; then try the next way round, so a player wedged between the
            // edge and the ground (the bottom of the sphere, a crack by a cliff) can still move along both.
            if (solid && collisions.IsColliding(api.World.BlockAccessor, entity.CollisionBox, feet, false))
            {
                continue;
            }

            stopped = feet;
            break;
        }

        Vec3d toFeet = new(to.X, to.Y - lift, to.Z);
        stopped ??= new Vec3d(start.X, start.Y - lift, start.Z);
        if (stopped.Equals(toFeet, 1e-9))
        {
            return;
        }

        Vec3d outward = toFeet - stopped;
        double length = outward.Length();
        if (length < 1e-6 || !IsFinite(outward))
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

        // Held from below (the bottom of the sphere, or a zone's floor): the edge is ground to stand, walk and jump on.
        if (walks && outward.Y < -0.6)
        {
            entity.OnGround = true;
            if (pos.Motion.Y < 0)
            {
                pos.Motion.Y = 0;
            }
        }

        Report(stopped.AddCopy(0, entity.LocalEyePos.Y * 0.6, 0));
    }

    /// <summary>
    /// Ways to end the step inside, best first: along the edge at the same height (walking), straight onto the edge, then
    /// only the parts of the step that do not lead out (one axis at a time), which also gets a player out of corners.
    /// </summary>
    private static IEnumerable<Vec3d> Candidates(LeashShape shape, Vec3d from, Vec3d to, bool walks)
    {
        if (walks)
        {
            yield return shape.Stop(from, to, true);
        }

        yield return shape.Stop(from, to, false);
        yield return new Vec3d(to.X, from.Y, to.Z);
        yield return new Vec3d(to.X, to.Y, from.Z);
        yield return new Vec3d(from.X, to.Y, to.Z);
        yield return new Vec3d(to.X, from.Y, from.Z);
        yield return new Vec3d(from.X, from.Y, to.Z);
        yield return new Vec3d(from.X, to.Y, from.Z);
    }

    private static bool IsFinite(Vec3d v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    private void Report(Vec3d at)
    {
        long now = api.World.ElapsedMilliseconds;
        if (now - lastHitMs >= HitIntervalMs)
        {
            lastHitMs = now;
            Hit?.Invoke(at);
        }
    }

    /// <summary>The last position the game had that was a number; see <see cref="LeashStepPatch"/>.</summary>
    private readonly Vec3d lastGood = new();
    private bool hasLastGood;

    /// <summary>
    /// Before each step: a position that is not a number crashes the game in its own physics (it happened at a zone's edge),
    /// so it is put back to the last good one, with no motion, instead.
    /// </summary>
    public void BeforeStep(EntityPos pos)
    {
        if (double.IsFinite(pos.X) && double.IsFinite(pos.Y) && double.IsFinite(pos.Z))
        {
            if (!double.IsFinite(pos.Motion.X) || !double.IsFinite(pos.Motion.Y) || !double.IsFinite(pos.Motion.Z))
            {
                pos.Motion.Set(0, 0, 0);
            }

            lastGood.Set(pos.X, pos.Y, pos.Z);
            hasLastGood = true;
            return;
        }

        if (hasLastGood)
        {
            api.Logger.ModWarning("the player's position became {0} {1} {2}; put back to {3}", pos.X, pos.Y, pos.Z, lastGood);
            pos.SetPos(lastGood.X, lastGood.Y, lastGood.Z);
            pos.Motion.Set(0, 0, 0);
        }
    }
}

/// <summary>Cosmetic for honest players: without it the server still pulls back whoever gets far outside.</summary>
[HarmonyPatch(typeof(EntityBehaviorPlayerPhysics), nameof(EntityBehaviorPlayerPhysics.SimPhysics))]
internal static class LeashStepPatch
{
    private static void Prefix(EntityBehaviorPlayerPhysics __instance, EntityPos pos, out Vec3d? __state)
    {
        LeashGuard? guard = TemporalRebirthModSystem.ClientLeash;
        if (guard != null && __instance.entity is EntityPlayer player && player == (player.World.Api as ICoreClientAPI)?.World.Player?.Entity)
        {
            guard.BeforeStep(pos);
        }

        __state = guard?.Shape == null ? null : new Vec3d(pos.X, pos.Y, pos.Z);
    }

    private static void Postfix(EntityBehaviorPlayerPhysics __instance, EntityPos pos, Vec3d? __state)
    {
        if (__state != null && __instance.entity is EntityPlayer player)
        {
            TemporalRebirthModSystem.ClientLeash?.AfterStep(player, pos, __state);
        }
    }
}
