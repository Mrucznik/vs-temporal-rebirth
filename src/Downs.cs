using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace TemporalRebirth;

internal enum LivesCount { Deaths, Downs, DownsFinalDeath }

/// <summary>
/// What takes a life when Lives.Count is Downs or DownsFinalDeath: going down in the Downed mod. At no lives the next
/// fatal hit, down or not, is the last.
///
/// A down that took a life is remembered on the player until they get up or die of it, so it is never charged twice:
/// Downed marks its attributes as changed every second and again after a relog.
/// </summary>
internal class Downs(ICoreServerAPI api, RebirthConfig config, Afterlife afterlife, Action<IServerPlayer> lastDown)
{
    private const string PaidKey = "temporalrebirth.paiddown";

    private readonly Dictionary<string, Action> listeners = new();

    /// <summary>Without Downed there are no downs, so deaths count.</summary>
    public LivesCount Mode => DownedCompat.IsInstalled(api) ? config.LivesCount : LivesCount.Deaths;

    public void Watch(IServerPlayer player)
    {
        string uid = player.PlayerUID;
        if (DownedCompat.IsInstalled(api))
        {
            // Also called for other attributes starting with "downed", and in the middle of Downed's own work: look at the
            // player a moment later, when Downed is done.
            if (!listeners.TryGetValue(uid, out Action? listener))
            {
                listeners[uid] = listener = () => api.Event.EnqueueMainThreadTask(() => Check(uid), "temporalrebirth-downs");
            }

            // After a quick relog the game may keep the entity but drop its listeners.
            player.Entity.WatchedAttributes.UnregisterListener(listener);
            player.Entity.WatchedAttributes.RegisterModifiedListener(DownedCompat.DownedAttr, listener);
        }

        Check(uid);
    }

    private void Check(string uid)
    {
        if (api.World.PlayerByUid(uid) is not IServerPlayer { Entity: not null, ConnectionState: EnumClientState.Connected or EnumClientState.Playing } player)
        {
            return;
        }

        bool paid = player.GetModData(PaidKey, false);
        if (!DownedCompat.IsDowned(player.Entity))
        {
            // Back on their feet. When dead instead, the death still has to see that it was paid for.
            if (paid && player.Entity.Alive)
            {
                player.SetModData(PaidKey, false);
            }

            return;
        }

        // Being downed while out of lives costs nothing.
        if (paid || !player.Entity.Alive || Mode == LivesCount.Deaths || LifeRules.UnlimitedLives(api.World) || afterlife.Contains(uid))
        {
            return;
        }

        if (LifeRules.GetLivesLeft(player) > 0)
        {
            player.SetModData(PaidKey, true);
            LifeRules.AddLives(player, -1);
        }
        else
        {
            lastDown(player);
        }
    }

    /// <summary>Whether the player died of a down that already took a life. Asking forgets it.</summary>
    public bool TakePaid(IServerPlayer player)
    {
        bool paid = player.GetModData(PaidKey, false);
        if (paid)
        {
            player.SetModData(PaidKey, false);
        }

        return paid;
    }

    /// <summary>Settles the cost of a death the game has just counted. Returns whether it was the last one.</summary>
    public bool ChargeDeath(IServerPlayer player)
    {
        bool paid = TakePaid(player);
        if (Mode == LivesCount.DownsFinalDeath)
        {
            LifeRules.SetLivesLeft(player, 0);
            return true;
        }

        if (paid)
        {
            // The game counts every death; give this one back.
            LifeRules.AddLives(player, 1);
            return false;
        }

        // Three lives are three deaths, the third being the last; but three downs and then one more.
        int left = LifeRules.RawLivesLeft(player);
        return Mode == LivesCount.Downs ? left < 0 : left <= 0;
    }
}
