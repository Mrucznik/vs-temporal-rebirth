using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalRebirth;

internal class Commands(ICoreServerAPI api, RebirthConfig config, ConfigFile file, Afterlife afterlife, Returns returns, Rituals rituals, Zones zones)
{
    private static readonly string[] PointNames = ["start", "return", "ritual"];

    public void Register()
    {
        var parsers = api.ChatCommands.Parsers;
        var root = api.ChatCommands.Create("rebirth").WithDescription("Temporal Rebirth: lives and what comes after them").RequiresPrivilege(Privilege.chat);

        root.BeginSubCommand("lives").WithDescription("Show how many lives you have left").RequiresPlayer().HandleWith(Lives).EndSubCommand();
        root.BeginSubCommand("ghosts").WithDescription("List players waiting to come back").HandleWith(Waiting).EndSubCommand();
        root.BeginSubCommand("target").WithDescription("Choose whom your next ritual brings back").RequiresPlayer()
            .WithArgs(parsers.Word("player")).HandleWith(Target).EndSubCommand();

        root.BeginSubCommand("revive").WithDescription("Admin: bring a player back before their time")
            .WithArgs(parsers.Word("player")).HandleWith(a => Guard(a, "Revive", Revive)).EndSubCommand();
        root.BeginSubCommand("setlives").WithDescription("Admin: set how many lives a player has left")
            .WithArgs(parsers.OnlinePlayer("player"), parsers.IntRange("lives", 0, 1000)).HandleWith(a => Guard(a, "SetLives", SetLives)).EndSubCommand();

        root.BeginSubCommand("preset").WithDescription("Admin: list presets or use one")
            .WithArgs(parsers.OptionalWord("name")).HandleWith(Preset).EndSubCommand();
        root.BeginSubCommand("config").WithDescription("Admin: show settings, or change one: /rebirth config <setting> <value> (\"\" = empty)")
            .WithArgs(parsers.OptionalWord("setting"), parsers.OptionalAll("value")).HandleWith(Config).EndSubCommand();
        root.BeginSubCommand("import").WithDescription("Admin: use a code made by the web configurator")
            .WithArgs(parsers.Word("code")).HandleWith(a => Guard(a, "Edit", Import)).EndSubCommand();

        root.BeginSubCommand("point").WithDescription("Admin: places for players out of lives, for coming back and for the ritual")
            .BeginSubCommand("set").WithArgs(parsers.WordRange("place", PointNames)).RequiresPlayer().HandleWith(a => Guard(a, "Points", SetPoint)).EndSubCommand()
            .BeginSubCommand("clear").WithArgs(parsers.WordRange("place", PointNames)).HandleWith(a => Guard(a, "Points", ClearPoint)).EndSubCommand()
            .BeginSubCommand("show").HandleWith(ShowPoints).EndSubCommand()
            .EndSubCommand();
    }

    private TextCommandResult Guard(TextCommandCallingArgs args, string action, System.Func<TextCommandCallingArgs, TextCommandResult> run) =>
        Rights.Refuse(config, args.Caller, action) is { } refusal ? Error(args, refusal) : run(args);

    // ---------- everyone ----------

    private TextCommandResult Lives(TextCommandCallingArgs args)
    {
        int left = LifeRules.GetLivesLeft((IServerPlayer)args.Caller.Player);
        return Ok(args, left < 0 ? "cmd-lives-unlimited" : "cmd-lives", left);
    }

    private TextCommandResult Waiting(TextCommandCallingArgs args)
    {
        if (afterlife.Players.Count == 0)
        {
            return Ok(args, "cmd-ghosts-none");
        }

        var text = new StringBuilder();
        foreach (LostPlayer lost in afterlife.Players.Values.OrderBy(p => p.DiedAtUtc))
        {
            string died = Messages.Time(lost.DiedAtUtc);
            text.AppendLine(lost.Phase == Phase.Returning ? T(args, "cmd-ghosts-returning", lost.Name)
                : lost.EndUtc(config) is { } at ? T(args, "cmd-ghosts-entry", lost.Name, died, Messages.Time(at))
                : T(args, "cmd-ghosts-ritual-only", lost.Name, died));
        }

        return Success(args, text.ToString().TrimEnd());
    }

    private TextCommandResult Target(TextCommandCallingArgs args)
    {
        var caster = (IServerPlayer)args.Caller.Player;
        if (afterlife.FindByName((string)args[0]) is not { } found || found.Key == caster.PlayerUID)
        {
            return Error(args, "cmd-not-ghost", args[0]);
        }

        rituals.Choose(caster, found.Key, found.Value);
        return Ok(args, "cmd-target-set", found.Value.Name);
    }

    // ---------- players ----------

    private TextCommandResult Revive(TextCommandCallingArgs args)
    {
        string name = (string)args[0];
        if (afterlife.FindByName(name) is { } found)
        {
            returns.ByAdmin(found.Key, found.Value);
            return Ok(args, "cmd-revived", found.Value.Name);
        }

        IServerPlayer? player = api.World.AllOnlinePlayers.Cast<IServerPlayer>().FirstOrDefault(p => p.PlayerName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (player == null || LifeRules.GetLivesLeft(player) != 0)
        {
            return Error(args, player == null ? "cmd-not-ghost" : "cmd-not-out-of-lives", name);
        }

        // Zero lives but still walking around: just give them back.
        LifeRules.SetLivesLeft(player, Math.Max(1, config.ReturnLives));
        return Ok(args, "cmd-revived", player.PlayerName);
    }

    private TextCommandResult SetLives(TextCommandCallingArgs args)
    {
        var target = (IServerPlayer)args[0];
        int lives = (int)args[1];
        if (LifeRules.UnlimitedLives(api.World))
        {
            return Error(args, "cmd-lives-unlimited");
        }

        if (lives > 0 && afterlife.Get(target.PlayerUID) is { } lost)
        {
            if (lost.Phase == Phase.Dying)
            {
                // Not turned into anything yet: the death becomes an ordinary one.
                afterlife.Remove(target.PlayerUID);
            }
            else
            {
                returns.ByAdmin(target.PlayerUID, lost, lives);
                return Ok(args, "cmd-lives-set", target.PlayerName, lives);
            }
        }

        // Zero lives while alive is allowed: the next death is the last.
        LifeRules.SetLivesLeft(target, lives);
        return Ok(args, "cmd-lives-set", target.PlayerName, lives);
    }

    // ---------- settings ----------

    private TextCommandResult Preset(TextCommandCallingArgs args)
    {
        string? name = args.Parsers[0].IsMissing ? null : (string)args[0];
        if (name == null)
        {
            return Success(args, T(args, "cmd-presets") + "\n" + string.Join("\n", ConfigSchema.Presets.Select(p => p.Id + ": " + T(args, "preset-" + p.Id.ToLowerInvariant()))));
        }

        return Guard(args, "Edit", _ =>
        {
            if (ConfigSchema.FindPreset(name) is not { } preset)
            {
                return Error(args, "cmd-preset-unknown", name, string.Join(", ", ConfigSchema.Presets.Select(p => p.Id)));
            }

            Use(ConfigSchema.Apply(preset, config.Values));
            return Success(args, T(args, "cmd-preset-set", preset.Id) + Reminders(args));
        });
    }

    private TextCommandResult Config(TextCommandCallingArgs args)
    {
        string? key = args.Parsers[0].IsMissing ? null : (string)args[0];
        if (key == null || key.Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            // Messages are long and have a page of their own in the configurator.
            var lines = ConfigSchema.Fields.Where(f => !f.IsMessage || config.Values[f.Key].ToString().Length > 0).Select(f => f.Key + " = " + Show(f.Key));
            return Success(args, string.Join("\n", lines) + Reminders(args));
        }

        if (!ConfigSchema.ByKey.TryGetValue(key, out ConfigField? field))
        {
            return Error(args, "cmd-config-unknown", key);
        }

        string text = args.Parsers[1].IsMissing ? "" : ((string)args[1]).Trim();
        if (text.Length == 0)
        {
            return Success(args, field.Key + " = " + Show(field.Key));
        }

        return Guard(args, "Edit", _ =>
        {
            if (field.FileOnly)
            {
                return Error(args, "cmd-config-file-only", field.Key);
            }

            // A pair of quotes stands for an empty text: back to the mod's own message.
            if (field.ParseAndValidate(text == "\"\"" ? "" : text, out string allowed) is not { } value)
            {
                return Error(args, "cmd-config-bad-value", field.Key, allowed);
            }

            Use(new Dictionary<string, JToken>(config.Values) { [field.Key] = value });
            return Success(args, T(args, "cmd-config-set", field.Key, Show(field.Key)) + Reminders(args));
        });
    }

    private TextCommandResult Import(TextCommandCallingArgs args)
    {
        var problems = new List<string>();
        if (ConfigFile.ReadCode((string)args[0], config.Values, problems) is not { } values)
        {
            return Error(args, "cmd-import-bad-code");
        }

        string[] changes = ConfigSchema.Fields
            .Where(f => !JToken.DeepEquals(config.Values[f.Key], values[f.Key]))
            .Select(f => $"{f.Key}: {Describe(args, f, config.Values[f.Key], false)} → {Describe(args, f, values[f.Key], true)}")
            .ToArray();
        string text = T(args, "cmd-import-same");
        if (changes.Length > 0)
        {
            Use(values);
            text = T(args, "cmd-import-done") + "\n" + T(args, "cmd-import-changes", string.Join(", ", changes)) + "\n" + T(args, "cmd-import-backup", file.BackupName);
        }

        return Success(args, text + string.Concat(problems.Select(p => "\n" + p)) + Reminders(args));
    }

    /// <summary>A value for the list of changes. Texts of admins can be long, so only the new one is shown.</summary>
    private static string Describe(TextCommandCallingArgs args, ConfigField field, JToken value, bool isNew)
    {
        string text = value.Type == JTokenType.String ? (string)value! : value.ToString(Formatting.None);
        if (field.IsMessage)
        {
            return text.Length == 0 ? T(args, "value-builtin") : isNew ? "\"" + text + "\"" : T(args, "value-custom");
        }

        return field.Special is { } special && special.Value == text ? T(args, "special-" + field.Key.ToLowerInvariant()) : text;
    }

    private void Use(IReadOnlyDictionary<string, JToken> values)
    {
        config.Replace(values);
        file.Write(values);
    }

    private string Show(string key) => config.Values[key].ToString(Formatting.None);

    /// <summary>Reminds of what these settings need but the server does not have: marked places, land claims, Downed.</summary>
    private string Reminders(TextCommandCallingArgs args)
    {
        var missing = new List<string>();
        if (config.Form != Form.Ban && config.PlaceAt is Place.Fixed or Place.Zone && afterlife.Point("start") == null)
        {
            missing.Add("start");
        }

        if (config.ReturnAt == Place.Fixed && afterlife.Point("return") == null)
        {
            missing.Add("return");
        }

        if (config.RitualWhere == RitualPlace.FixedPlace && afterlife.Point("ritual") == null)
        {
            missing.Add("ritual");
        }

        string text = string.Concat(missing.Select(m => "\n" + T(args, "cmd-point-missing", m)));
        if (config.Form != Form.Ban && config.PlaceAt == Place.Zone)
        {
            List<LandClaim> claims = zones.Claims(config.PlaceGroup);
            text += claims.Count == 0 ? "\n" + T(args, "cmd-zone-missing", config.PlaceGroup)
                : afterlife.Point("start") is { } start && !Zones.Contains(claims, start) ? "\n" + T(args, "cmd-zone-start-outside")
                : "";
        }

        return config.LivesCount != LivesCount.Deaths && !DownedCompat.IsInstalled(api) ? text + "\n" + T(args, "cmd-downed-missing") : text;
    }

    // ---------- places ----------

    private TextCommandResult SetPoint(TextCommandCallingArgs args)
    {
        Vec3d pos = args.Caller.Player.Entity.Pos.XYZ;
        afterlife.SetPoint((string)args[0], pos);
        return Ok(args, "cmd-point-set", args[0], Local(pos));
    }

    private TextCommandResult ClearPoint(TextCommandCallingArgs args)
    {
        afterlife.SetPoint((string)args[0], null);
        return Ok(args, "cmd-point-cleared", args[0]);
    }

    private TextCommandResult ShowPoints(TextCommandCallingArgs args) =>
        Success(args, string.Join("\n", PointNames.Select(n => n + ": " + (afterlife.Point(n) is { } p ? Local(p) : T(args, "cmd-point-none")))));

    /// <summary>The coordinates players see in game.</summary>
    private string Local(Vec3d pos) => pos.AsBlockPos.ToLocalPosition(api).ToString();

    /// <summary>Translates for the calling player, or in the server language for the console.</summary>
    private static string T(TextCommandCallingArgs args, string key, params object[] values)
    {
        string fullKey = "temporalrebirth:" + key;
        return args.Caller.Player is IServerPlayer player
            ? Lang.GetL(player.LanguageCode, fullKey, values)
            : Lang.Get(fullKey, values);
    }

    private static TextCommandResult Ok(TextCommandCallingArgs args, string key, params object[] values) => Success(args, T(args, key, values));

    private static TextCommandResult Success(TextCommandCallingArgs args, string text) => TextCommandResult.Success(Safe(args, text));

    private static TextCommandResult Error(TextCommandCallingArgs args, string key, params object[] values) => TextCommandResult.Error(Safe(args, T(args, key, values)));

    /// <summary>The server console treats answers as format strings and drops any with a lone {placeholder}.</summary>
    private static string Safe(TextCommandCallingArgs args, string text) =>
        args.Caller.Type == EnumCallerType.Console ? text.Replace("{", "{{").Replace("}", "}}") : text;
}
