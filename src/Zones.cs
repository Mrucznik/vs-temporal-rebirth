using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace TemporalRebirth;

internal enum ZoneAccess { Traverse, Use, All }

/// <summary>
/// Place.At Zone: the land claims shared with one player group. Claims only give the shape; what players out of lives may
/// do inside comes from Place.Access, whatever the claims say, and everyone else keeps the claims' own rules.
/// </summary>
internal class Zones(ICoreServerAPI api, Afterlife afterlife)
{
    /// <summary>Set after construction; the two need each other.</summary>
    public Rebirth Rebirth { get; set; } = null!;

    public void Register() => api.Event.OnTestBlockAccessClaim += TestAccess;

    public void Unregister() => api.Event.OnTestBlockAccessClaim -= TestAccess;

    public int? GroupId(string group) => api.Groups.GetPlayerGroupByName(group)?.Uid;

    public List<LandClaim> Claims(string group) =>
        GroupId(group) is { } id ? api.World.Claims.All.Where(c => IsPart(c, id)).ToList() : [];

    public static bool Contains(List<LandClaim> claims, Vec3d pos) => claims.Any(c => c.PositionInside(pos));

    /// <summary>The spot in the zone closest to <paramref name="pos"/>, on the ground where the land is loaded.</summary>
    public Vec3d Nearest(List<LandClaim> claims, Vec3d pos)
    {
        Vec3d spot = claims.SelectMany(c => c.Areas).Select(a => Clamp(a, pos)).MinBy(p => p.SquareDistanceTo(pos))!;
        return Ground(claims, spot);
    }

    /// <summary>The ground at a spot of the zone; the spot itself if the ground is not loaded or outside the zone.</summary>
    public Vec3d Ground(List<LandClaim> claims, Vec3d spot) =>
        SafeSpot.FindGround(api.World.BlockAccessor, spot) is { } ground && Contains(claims, ground) ? ground : spot;

    private static Vec3d Clamp(Cuboidi area, Vec3d pos) => new(
        Math.Clamp(pos.X, area.MinX + 0.5, area.MaxX - 0.5),
        Math.Clamp(pos.Y, area.MinY, Math.Max(area.MinY, area.MaxY - 2)),
        Math.Clamp(pos.Z, area.MinZ + 0.5, area.MaxZ - 0.5));

    internal static bool IsPart(LandClaim claim, int group) => claim.PermittedPlayerGroupIds?.ContainsKey(group) == true;

    // ---------- access ----------

    private EnumWorldAccessResponse TestAccess(IPlayer player, BlockSelection selection, EnumBlockAccessFlags wanted, ref string claimant, LandClaim claim, EnumWorldAccessResponse response)
    {
        if (afterlife.Get(player.PlayerUID) is not { Phase: Phase.Serving } lost)
        {
            return response;
        }

        RebirthConfig rules = Rebirth.Rules(lost);
        if (rules.PlaceAt != Place.Zone || !Rebirth.Walks(rules.Form) || GroupId(rules.PlaceGroup) is not { } group)
        {
            return response;
        }

        EnumWorldAccessResponse decided = Decide(api.World.Claims, selection.Position, group, rules.PlaceAccess, wanted, claim, response, ref claimant);
        if (decided == EnumWorldAccessResponse.Granted && response != EnumWorldAccessResponse.Granted && player is IServerPlayer serverPlayer)
        {
            // The claim stopped the game before it asked mods, including the limits of zombies.
            var events = ((ServerMain)api.World).EventManager;
            bool agreed = wanted == EnumBlockAccessFlags.Use
                ? events.TriggerCanUse(serverPlayer, selection)
                : events.TriggerCanPlaceOrBreak(serverPlayer, selection, out claimant);
            return agreed ? decided : EnumWorldAccessResponse.DeniedByMod;
        }

        return decided;
    }

    /// <summary>The same decision on both sides, so a client predicts what the server will say.</summary>
    internal static EnumWorldAccessResponse Decide(ILandClaimAPI claims, BlockPos pos, int group, ZoneAccess access, EnumBlockAccessFlags wanted, LandClaim? blocking, EnumWorldAccessResponse response, ref string claimant)
    {
        // Spectators, dead players, missing privileges: not about claims. Nor are claims of others inside the zone.
        if (response is not (EnumWorldAccessResponse.Granted or EnumWorldAccessResponse.LandClaimed) || (blocking != null && !IsPart(blocking, group)))
        {
            return response;
        }

        LandClaim? part = claims.Get(pos)?.FirstOrDefault(c => IsPart(c, group));
        if (part == null)
        {
            return response;
        }

        bool allowed = wanted == EnumBlockAccessFlags.Use ? access != ZoneAccess.Traverse : access == ZoneAccess.All;
        if (!allowed)
        {
            claimant = part.LastKnownOwnerName;
        }

        return allowed ? EnumWorldAccessResponse.Granted : EnumWorldAccessResponse.LandClaimed;
    }
}
