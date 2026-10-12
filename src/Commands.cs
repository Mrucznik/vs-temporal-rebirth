using System.Text;
using Vintagestory.API.Common;
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
                return TextCommandResult.Success(left < 0 ? "Unlimited lives." : $"Lives left: {left}");
            })
            .EndSubCommand()
            .BeginSubCommand("ghosts")
            .WithDescription("List players waiting to come back")
            .HandleWith(_ =>
            {
                if (rebirth.Afterlife.Ghosts.Count == 0)
                {
                    return TextCommandResult.Success("Nobody is waiting.");
                }

                var sb = new StringBuilder();
                foreach (var (_, ghost) in rebirth.Afterlife.Ghosts.OrderBy(g => g.Value.DiedAtUtc))
                {
                    DateTime? at = rebirth.ReturnAtUtc(ghost);
                    string back = ghost.RevivedByRitual ? "revived, back on next login"
                        : at == null ? "only by ritual"
                        : $"back at {at.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
                    sb.AppendLine($"{ghost.Name}: died {ghost.DiedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}, {back}");
                }

                return TextCommandResult.Success(sb.ToString().TrimEnd());
            })
            .EndSubCommand()
            .BeginSubCommand("revive")
            .WithDescription("Bring a ghost back to life where they are (admin)")
            .RequiresPrivilege(Privilege.controlserver)
            .WithArgs(parsers.OnlinePlayer("player"))
            .HandleWith(args =>
            {
                var target = (IServerPlayer)args[0];
                if (LifeRules.GetLivesLeft(target) != 0 && !rebirth.Afterlife.IsGhost(target.PlayerUID))
                {
                    return TextCommandResult.Error($"{target.PlayerName} is not out of lives.");
                }

                rebirth.AdminRevive(target);
                return TextCommandResult.Success($"{target.PlayerName} is back.");
            })
            .EndSubCommand();
    }
}
