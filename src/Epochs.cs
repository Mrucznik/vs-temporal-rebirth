using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>
/// Tracks "lives epochs" in the savegame: a new epoch starts whenever the world switches from
/// unlimited to limited lives, including the first start with this mod on a limited world.
/// Players get a fresh death baseline in each epoch (see <see cref="LifeRules.EnsureBaseline"/>).
/// </summary>
internal static class Epochs
{
    private const string EpochKey = "temporalrebirth-epoch";
    private const string LimitedKey = "temporalrebirth-limited";

    internal static int Advance(ICoreServerAPI api)
    {
        var save = api.WorldManager.SaveGame;
        int epoch = ReadInt(save.GetData(EpochKey), 0);
        bool wasLimited = ReadInt(save.GetData(LimitedKey), 0) == 1;
        // The real world setting, not UnlimitedLives(): a temporarily disabled mod must not start a new epoch.
        bool isLimited = LifeRules.WorldLives(api.World) >= 0;

        if (isLimited && !wasLimited)
        {
            epoch++;
            api.Logger.Notification("[temporalrebirth] World now has limited lives; starting lives epoch {0}, death counts restart.", epoch);
        }

        save.StoreData(EpochKey, BitConverter.GetBytes(epoch));
        save.StoreData(LimitedKey, BitConverter.GetBytes(isLimited ? 1 : 0));
        return epoch;
    }

    private static int ReadInt(byte[]? data, int fallback) =>
        data is { Length: 4 } ? BitConverter.ToInt32(data, 0) : fallback;
}
