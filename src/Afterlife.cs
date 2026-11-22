using System.Text.Json;
using System.Text.Json.Serialization;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>Where a player is between losing the last life and being back. Saved, so any moment survives a relog or restart.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum Phase
{
    /// <summary>The last death was counted; the player has not been turned into anything yet.</summary>
    Dying,

    /// <summary>Out of lives: ghost, zombie, banned...</summary>
    Serving,

    /// <summary>Time is up or someone helped; not yet alive at the destination.</summary>
    Returning,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum ReturnReason { Time, Ritual, Admin, Released }

/// <summary>A player who ran out of lives.</summary>
internal sealed class LostPlayer
{
    public string Name { get; set; } = "";
    public DateTime DiedAtUtc { get; set; }

    /// <summary>Null for ghosts from before 0.2.0.</summary>
    public double[]? DeathPos { get; set; }

    /// <summary>Entries written by 0.6.x have no phase; those players are ghosts already.</summary>
    public Phase Phase { get; set; } = Phase.Serving;

    /// <summary>The settings at the time of death, see <see cref="RebirthConfig.Snapshot"/>. Null for 0.6.x entries.</summary>
    public string? Settings { get; set; }

    /// <summary>Where they stay; the distance limit counts from here.</summary>
    public double[]? Start { get; set; }

    /// <summary>On the way to <see cref="Start"/>, not yet let loose there.</summary>
    public bool Travelling { get; set; }

    /// <summary>When a walking player was last let loose; dying again right after means the place is deadly.</summary>
    public DateTime? LandedAtUtc { get; set; }

    /// <summary>Moved to their spawn point, because where they died is no place to be.</summary>
    public bool AtSpawn { get; set; }

    public ReturnReason Reason { get; set; }

    /// <summary>Who helped, for the message.</summary>
    public string? Helper { get; set; }

    /// <summary>Where to; null means wherever the player is.</summary>
    public double[]? Destination { get; set; }

    /// <summary>To their spawn point instead, which is only known while they are online.</summary>
    public bool ToSpawn { get; set; }

    /// <summary>Lives to come back with, when an admin chose a number.</summary>
    public int? Lives { get; set; }

    [JsonIgnore]
    public Vec3d? StartVec => Start is { Length: 3 } p ? new Vec3d(p[0], p[1], p[2]) : null;

    [JsonIgnore]
    public Vec3d? DeathVec => DeathPos is { Length: 3 } p ? new Vec3d(p[0], p[1], p[2]) : null;

    [JsonIgnore]
    public Vec3d? DestinationVec => Destination is { Length: 3 } p ? new Vec3d(p[0], p[1], p[2]) : null;

    public static double[] Pack(Vec3d p) => [p.X, p.Y, p.Z];
}

/// <summary>What the mod keeps in the savegame: players out of lives, marked places, ritual cooldowns.</summary>
internal class Afterlife(ICoreServerAPI api)
{
    private const string PlayersKey = "temporalrebirth-ghosts";
    private const string PointsKey = "temporalrebirth-points";
    private const string RitualsKey = "temporalrebirth-rituals";

    private Dictionary<string, LostPlayer> players = new();
    private Dictionary<string, double[]> points = new();
    private Dictionary<string, DateTime> rituals = new();

    public IReadOnlyDictionary<string, LostPlayer> Players => players;

    public IReadOnlyDictionary<string, double[]> Points => points;

    public void Load()
    {
        players = Read<LostPlayer>(PlayersKey);
        points = Read<double[]>(PointsKey);
        rituals = Read<DateTime>(RitualsKey);
        // A quick relog in 0.6.x could leave entries without a name.
        foreach (string uid in players.Where(p => p.Value.Name.Length == 0).Select(p => p.Key).ToList())
        {
            players[uid].Name = api.PlayerData.GetPlayerDataByUid(uid)?.LastKnownPlayername ?? uid;
        }
    }

    /// <summary>Entries of 0.6.x have no settings of their own; from now on they keep <paramref name="settings"/>.</summary>
    public void PinOldEntries(string settings)
    {
        foreach (LostPlayer lost in players.Values)
        {
            lost.Settings ??= settings;
        }

        Save();
    }

    public void Save()
    {
        Store(PlayersKey, players);
        Store(PointsKey, points);
        Store(RitualsKey, rituals);
    }

    public bool Contains(string uid) => players.ContainsKey(uid);

    public LostPlayer? Get(string uid) => players.GetValueOrDefault(uid);

    public KeyValuePair<string, LostPlayer>? FindByName(string name)
    {
        foreach (var pair in players)
        {
            if (pair.Value.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return pair;
            }
        }

        return null;
    }

    public void Add(string uid, string name, Vec3d deathPos, string settings)
    {
        players[uid] = new LostPlayer { Name = name, DiedAtUtc = DateTime.UtcNow, DeathPos = LostPlayer.Pack(deathPos), Phase = Phase.Dying, Settings = settings };
        Save();
    }

    public void Remove(string uid)
    {
        if (players.Remove(uid))
        {
            Save();
        }
    }

    public Vec3d? Point(string name) => points.TryGetValue(name, out double[]? p) && p.Length == 3 ? new Vec3d(p[0], p[1], p[2]) : null;

    public void SetPoint(string name, Vec3d? pos)
    {
        if (pos == null)
        {
            points.Remove(name);
        }
        else
        {
            points[name] = LostPlayer.Pack(pos);
        }

        Save();
    }

    public DateTime? LastRitual(string uid) => rituals.TryGetValue(uid, out DateTime at) ? at : null;

    public void RitualDone(string uid)
    {
        rituals[uid] = DateTime.UtcNow;
        Save();
    }

    private Dictionary<string, T> Read<T>(string key)
    {
        byte[]? data = api.WorldManager.SaveGame.GetData(key);
        if (data == null)
        {
            return new();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, T>>(data) ?? new();
        }
        catch (JsonException e)
        {
            api.Logger.Error("[temporalrebirth] Could not read {0} from the savegame, starting empty: {1}", key, e.Message);
            return new();
        }
    }

    private void Store<T>(string key, Dictionary<string, T> data) =>
        api.WorldManager.SaveGame.StoreData(key, JsonSerializer.SerializeToUtf8Bytes(data));
}
