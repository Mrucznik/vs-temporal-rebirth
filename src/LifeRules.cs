using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace TemporalRebirth;

/// <summary>
/// Lives left = world "playerlives" + lives earned or granted by this mod - deaths.
/// Vanilla keeps the death counter in WorldData.Deaths; we never touch it, we only add lives.
/// </summary>
internal static class LifeRules
{
    internal const string ExtraLivesKey = "temporalrebirth.extralives";

    /// <summary>Synced to the client so the death screen shows the real count.</summary>
    internal const string LivesLeftAttr = "temporalrebirthLivesLeft";

    internal static int WorldLives(IWorldAccessor world) =>
        world.Config?.GetString("playerlives", "-1").ToInt(-1) ?? -1;

    internal static bool UnlimitedLives(IWorldAccessor world) => WorldLives(world) < 0;

    internal static int GetExtraLives(IServerPlayer player) => player.GetModData(ExtraLivesKey, 0);

    /// <summary>-1 means unlimited.</summary>
    internal static int GetLivesLeft(IServerPlayer player)
    {
        int worldLives = WorldLives(player.Entity.World);
        if (worldLives < 0)
        {
            return -1;
        }

        return Math.Max(0, worldLives + GetExtraLives(player) - player.WorldData.Deaths);
    }

    internal static void AddLives(IServerPlayer player, int amount)
    {
        player.SetModData(ExtraLivesKey, GetExtraLives(player) + amount);
        Sync(player);
    }

    /// <summary>Gives a player out of lives exactly one life back.</summary>
    internal static void GrantSecondChance(IServerPlayer player)
    {
        int livesLeft = GetLivesLeft(player);
        if (livesLeft == 0)
        {
            AddLives(player, 1);
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
