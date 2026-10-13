using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace TemporalRebirth;

internal static class Commands
{
    internal static void Register(ICoreServerAPI api, Rebirth rebirth)
    {
        var parsers = api.ChatCommands.Parsers;

        api.ChatCommands
            .Create("rebirth")
            .WithDescription("Temporal Rebirth: lives and ghosts")
            .RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("lives")
            .WithDescription("Show how many lives you have left")
            .RequiresPlayer()
            .HandleWith(args =>
            {
                var player = (IServerPlayer)args.Caller.Player;
                int left = LifeRules.GetLivesLeft(player);
                return Ok(args, left < 0 ? "cmd-lives-unlimited" : "cmd-lives", left);
            })
            .EndSubCommand()
            .BeginSubCommand("ghosts")
            .WithDescription("List players waiting to come back")
            .HandleWith(args =>
            {
                if (rebirth.Afterlife.Ghosts.Count == 0)
                {
                    return Ok(args, "cmd-ghosts-none");
                }

                var sb = new StringBuilder();
                foreach (var (_, ghost) in rebirth.Afterlife.Ghosts.OrderBy(g => g.Value.DiedAtUtc))
                {
                    string died = ghost.DiedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                    DateTime? at = rebirth.ReturnAtUtc(ghost);
                    sb.AppendLine(at == null
                        ? T(args, "cmd-ghosts-ritual-only", ghost.Name, died)
                        : T(args, "cmd-ghosts-entry", ghost.Name, died, at.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")));
                }

                return TextCommandResult.Success(sb.ToString().TrimEnd());
            })
            .EndSubCommand()
            .BeginSubCommand("target")
            .WithDescription("Choose which ghost your next ritual brings back")
            .RequiresPlayer()
            .WithArgs(parsers.OnlinePlayer("ghost"))
            .HandleWith(args =>
            {
                var caster = (IServerPlayer)args.Caller.Player;
                var ghost = (IServerPlayer)args[0];
                if (!rebirth.Afterlife.IsGhost(ghost.PlayerUID))
                {
                    return Error(args, "cmd-not-ghost", ghost.PlayerName);
                }

                rebirth.ChooseRitualTarget(caster, ghost);
                return Ok(args, "cmd-target-set", ghost.PlayerName);
            })
            .EndSubCommand()
            .BeginSubCommand("revive")
            .WithDescription("Admin: bring a ghost back to life where they are")
            .RequiresPrivilege(Privilege.controlserver)
            .WithArgs(parsers.OnlinePlayer("player"))
            .HandleWith(args =>
            {
                var target = (IServerPlayer)args[0];
                if (LifeRules.GetLivesLeft(target) != 0 && !rebirth.Afterlife.IsGhost(target.PlayerUID))
                {
                    return Error(args, "cmd-not-out-of-lives", target.PlayerName);
                }

                rebirth.AdminRevive(target);
                return Ok(args, "cmd-revived", target.PlayerName);
            })
            .EndSubCommand()
            .BeginSubCommand("setlives")
            .WithDescription("Admin: set how many lives a player has left")
            .RequiresPrivilege(Privilege.controlserver)
            .WithArgs(parsers.OnlinePlayer("player"), parsers.IntRange("lives", 0, 1000))
            .HandleWith(args =>
            {
                var target = (IServerPlayer)args[0];
                int lives = (int)args[1];
                if (LifeRules.UnlimitedLives(api.World))
                {
                    return Error(args, "cmd-lives-unlimited");
                }

                // Zero lives while alive is allowed: the next death makes them a ghost.
                if (lives > 0 && rebirth.Afterlife.IsGhost(target.PlayerUID))
                {
                    rebirth.AdminRevive(target);
                }

                LifeRules.SetLivesLeft(target, lives);
                return Ok(args, "cmd-lives-set", target.PlayerName, lives);
            })
            .EndSubCommand();
    }

    /// <summary>Translates for the calling player, or in the server language for the console.</summary>
    private static string T(TextCommandCallingArgs args, string key, params object[] values)
    {
        string fullKey = "temporalrebirth:" + key;
        return args.Caller.Player is IServerPlayer player
            ? Lang.GetL(player.LanguageCode, fullKey, values)
            : Lang.Get(fullKey, values);
    }

    private static TextCommandResult Ok(TextCommandCallingArgs args, string key, params object[] values) =>
        TextCommandResult.Success(T(args, key, values));

    private static TextCommandResult Error(TextCommandCallingArgs args, string key, params object[] values) =>
        TextCommandResult.Error(T(args, key, values));
}
