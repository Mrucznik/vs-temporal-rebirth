using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace TemporalRebirth;

/// <summary>
/// Lives left = world "playerlives" + lives earned or granted by this mod - deaths since the epoch began.
///
/// Vanilla keeps an all-time death counter in WorldData.Deaths and we never touch it. An existing world
/// switched to limited lives would otherwise start everyone at zero, so we count deaths from a per-player
/// baseline, taken on the first login in the current "lives epoch". The epoch advances whenever the world
/// goes from unlimited to limited lives (including the first start with this mod).
/// </summary>
internal static class LifeRules
{
    internal const string ExtraLivesKey = "temporalrebirth.extralives";
    internal const string DeathsBaselineKey = "temporalrebirth.deathsbaseline";
    internal const string EpochKey = "temporalrebirth.epoch";

    /// <summary>Synced to the client so the death screen shows the real count.</summary>
    internal const string LivesLeftAttr = "temporalrebirthLivesLeft";

    /// <summary>Set by the mod system from the savegame on startup.</summary>
    internal static int CurrentEpoch { get; set; }

    internal static int WorldLives(IWorldAccessor world) =>
        world.Config?.GetString("playerlives", "-1").ToInt(-1) ?? -1;

    internal static bool UnlimitedLives(IWorldAccessor world) => WorldLives(world) < 0;

    internal static int GetExtraLives(IServerPlayer player) => player.GetModData(ExtraLivesKey, 0);

    /// <summary>Starts counting deaths from now if the player has not been seen in the current epoch.</summary>
    internal static void EnsureBaseline(IServerPlayer player)
    {
        if (player.GetModData(EpochKey, 0) == CurrentEpoch)
        {
            return;
        }

        player.SetModData(EpochKey, CurrentEpoch);
        player.SetModData(DeathsBaselineKey, player.WorldData.Deaths);
        player.SetModData(ExtraLivesKey, 0);
    }

    internal static int CountedDeaths(IServerPlayer player) =>
        Math.Max(0, player.WorldData.Deaths - player.GetModData(DeathsBaselineKey, 0));

    /// <summary>Can be negative when a player died more often than they had lives.</summary>
    private static int RawLivesLeft(IServerPlayer player) =>
        WorldLives(player.Entity.World) + GetExtraLives(player) - CountedDeaths(player);

    /// <summary>-1 means unlimited.</summary>
    internal static int GetLivesLeft(IServerPlayer player)
    {
        if (UnlimitedLives(player.Entity.World))
        {
            return -1;
        }

        return Math.Max(0, RawLivesLeft(player));
    }

    internal static void AddLives(IServerPlayer player, int amount)
    {
        player.SetModData(ExtraLivesKey, GetExtraLives(player) + amount);
        Sync(player);
    }

    /// <summary>Sets lives left to exactly <paramref name="lives"/>.</summary>
    internal static void SetLivesLeft(IServerPlayer player, int lives)
    {
        AddLives(player, lives - RawLivesLeft(player));
    }

    /// <summary>Gives a player out of lives exactly one life back.</summary>
    internal static void GrantSecondChance(IServerPlayer player)
    {
        if (!UnlimitedLives(player.Entity.World) && RawLivesLeft(player) < 1)
        {
            SetLivesLeft(player, 1);
        }
    }

    internal static void Sync(IServerPlayer player)
    {
        if (player.Entity == null)
        {
            return;
        }

        player.Entity.WatchedAttributes.SetInt(LivesLeftAttr, GetLivesLeft(player));
        player.Entity.WatchedAttributes.MarkPathDirty(LivesLeftAttr);
    }
}
