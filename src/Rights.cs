using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>
/// Who may do what in game. The file's Admin switches say whether an action may be used at all; the game's own
/// privileges say by whom. The server console is never limited.
/// </summary>
internal static class Rights
{
    internal static void Register(ICoreServerAPI api)
    {
        foreach (ConfigField field in ConfigSchema.Fields.Where(f => f.Privilege != null))
        {
            api.Permissions.RegisterPrivilege(field.Privilege!, "Temporal Rebirth: " + field.Privilege!.Split('.')[1]);
        }
    }

    /// <summary><paramref name="action"/> as in "Admin.Allow" + action. Returns null when allowed, else a lang key.</summary>
    internal static string? Refuse(RebirthConfig config, Caller caller, string action)
    {
        if (caller.Type == EnumCallerType.Console)
        {
            return null;
        }

        if (!config.Allows(action))
        {
            return "cmd-action-off";
        }

        string privilege = ConfigSchema.ByKey["Admin.Allow" + action].Privilege!;
        return caller.HasPrivilege(privilege) || caller.HasPrivilege(Privilege.controlserver) ? null : "cmd-no-permission";
    }
}
