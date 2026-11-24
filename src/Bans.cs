using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace TemporalRebirth;

/// <summary>Players out of lives who may not be on the server at all.</summary>
internal class Bans(ICoreServerAPI api, RebirthConfig config, Afterlife afterlife, Messages messages)
{
    /// <summary>What a player trying to join is told, or null when they may join.</summary>
    public string? RefuseJoin(string? uid)
    {
        if (uid == null || LifeRules.UnlimitedLives(api.World) || afterlife.Get(uid) is not { } lost || lost.Phase == Phase.Returning || afterlife.Rules(lost).Form != Form.Ban)
        {
            return null;
        }

        // Dying: not turned into anything yet, but the ban already counts.
        return afterlife.EndUtc(lost) is { } at && DateTime.UtcNow >= at ? null : BanText(null, lost);
    }

    private string BanText(string? language, LostPlayer lost)
    {
        if (afterlife.EndUtc(lost) is { } at)
        {
            return messages.Text(language, "Ban", ("time", Messages.Time(at)), ("player", lost.Name));
        }

        // Without an end there is no time to fill in, unless the admin wrote a text of their own.
        return config.Message("Ban") != null ? messages.Text(language, "Ban", ("time", "-"), ("player", lost.Name)) : Lang.GetL(language ?? Lang.CurrentLocale, "temporalrebirth:ban-forever");
    }

    public void Kick(string uid, LostPlayer lost)
    {
        // IServerPlayer.Disconnect(message) only writes the message to the log (1.22.7); this overload shows it.
        var server = (ServerMain)api.World;
        ConnectedClient? client = server.Clients.Values.FirstOrDefault(c => c.Player?.PlayerUID == uid);
        if (client != null)
        {
            server.DisconnectPlayer(client, null, BanText(client.Player.LanguageCode, lost));
        }
    }
}
