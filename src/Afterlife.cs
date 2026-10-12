using System.Text.Json;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>A player who ran out of lives and now haunts the world as a spectator.</summary>
public class Ghost
{
    public string Name { get; set; } = "";
    public DateTime DiedAtUtc { get; set; }

    /// <summary>Where the player died; the ghost cannot stray far from it. Null for ghosts from before 0.2.0.</summary>
    public double[]? DeathPos { get; set; }

    /// <summary>Set by a ritual performed while the ghost was offline; applied on their next login.</summary>
    public bool RevivedByRitual { get; set; }

    public double RitualX { get; set; }
    public double RitualY { get; set; }
    public double RitualZ { get; set; }
}

/// <summary>Registry of ghosts, stored in the savegame so it survives restarts and covers offline players.</summary>
internal class Afterlife
{
    private const string SaveKey = "temporalrebirth-ghosts";

    private readonly ICoreServerAPI api;
    private Dictionary<string, Ghost> ghosts = new();

    public Afterlife(ICoreServerAPI api)
    {
        this.api = api;
    }

    public IReadOnlyDictionary<string, Ghost> Ghosts => ghosts;

    public void Load()
    {
        byte[]? data = api.WorldManager.SaveGame.GetData(SaveKey);
        if (data == null)
        {
            return;
        }

        try
        {
            ghosts = JsonSerializer.Deserialize<Dictionary<string, Ghost>>(data) ?? new();
        }
        catch (JsonException e)
        {
            api.Logger.Error("[temporalrebirth] Could not read the ghost registry, starting empty: {0}", e.Message);
            ghosts = new();
        }
    }

    public void Save()
    {
        api.WorldManager.SaveGame.StoreData(SaveKey, JsonSerializer.SerializeToUtf8Bytes(ghosts));
    }

    public bool IsGhost(string uid) => ghosts.ContainsKey(uid);

    public Ghost? Get(string uid) => ghosts.GetValueOrDefault(uid);

    public void Add(string uid, string name, Vec3d deathPos)
    {
        ghosts[uid] = new Ghost
        {
            Name = name,
            DiedAtUtc = DateTime.UtcNow,
            DeathPos = [deathPos.X, deathPos.Y, deathPos.Z],
        };
        Save();
    }

    public void Remove(string uid)
    {
        if (ghosts.Remove(uid))
        {
            Save();
        }
    }

    /// <summary>Ghost who has waited longest and is not already promised a revival.</summary>
    public KeyValuePair<string, Ghost>? NextRitualTarget(bool includeOffline, Func<string, bool> isOnline)
    {
        foreach (var pair in ghosts.OrderBy(p => p.Value.DiedAtUtc))
        {
            if (pair.Value.RevivedByRitual)
            {
                continue;
            }

            if (includeOffline || isOnline(pair.Key))
            {
                return pair;
            }
        }

        return null;
    }
}
