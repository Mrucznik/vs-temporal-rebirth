using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>
/// Texts players see. The mod's own come from lang files in the player's language ("msg-" + key, arguments in the
/// order of the schema's vars); an admin's own text replaces them for everyone.
/// </summary>
internal class Messages(ICoreServerAPI api, RebirthConfig config)
{
    public void Tell(IServerPlayer player, string key, params (string Name, object Value)[] args) =>
        player.SendMessage(GlobalConstants.GeneralChatGroup, Text(player.LanguageCode, key, args), EnumChatType.Notification);

    /// <summary>A text admins cannot replace (short feedback to one player), straight from the lang files.</summary>
    public void TellText(IServerPlayer player, string langKey, params object[] args) =>
        player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "temporalrebirth:" + langKey, args), EnumChatType.Notification);

    public void TellEveryone(string key, params (string Name, object Value)[] args)
    {
        foreach (IServerPlayer player in api.World.AllOnlinePlayers.Cast<IServerPlayer>())
        {
            Tell(player, key, args);
        }
    }

    public string Text(string? language, string key, params (string Name, object Value)[] args)
    {
        if (config.Message(key) is { } custom)
        {
            foreach (var (name, value) in args)
            {
                custom = custom.Replace("{" + name + "}", value.ToString());
            }

            return custom;
        }

        // Lang files use numbered arguments, in the order the schema lists the placeholders.
        string[] vars = ConfigSchema.ByKey["Messages." + key].Vars!;
        object[] ordered = vars.Select(v => args.FirstOrDefault(a => a.Name == v).Value ?? "").ToArray();
        return Lang.GetL(language ?? Lang.CurrentLocale, "temporalrebirth:msg-" + key.ToLowerInvariant(), ordered);
    }

    public static string Time(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
