using System.Reflection;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalRebirth.TestHelper;

/// <summary>
/// Console commands for scenarios. Every command answers with one log line: "TRT {json}".
/// Reaches into Temporal Rebirth by reflection only, so it builds without it.
/// </summary>
public sealed class HelperModSystem : ModSystem
{
    private ICoreServerAPI api = null!;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI sapi)
    {
        api = sapi;
        var parsers = api.ChatCommands.Parsers;
        var root = api.ChatCommands.Create("trt").WithDescription("Test helper").RequiresPrivilege(Privilege.controlserver);

        void Add(string name, System.Func<TextCommandCallingArgs, object> action, params ICommandArgumentParser[] args) =>
            root.BeginSubCommand(name).WithArgs(args).HandleWith(a =>
            {
                object answer;
                try
                {
                    answer = action(a);
                }
                catch (Exception e)
                {
                    answer = new { error = e.ToString() };
                }

                api.Logger.Notification("TRT {0}", JsonConvert.SerializeObject(answer));
                return TextCommandResult.Success();
            }).EndSubCommand();

        Add("state", a => State(Player(a)), parsers.Word("player"));
        Add("gear", a => UseGear(Player(a)), parsers.Word("player"));
        Add("rift", a => AddRift(Player(a), (int)a[1]), parsers.Word("player"), parsers.Int("distance"));
        Add("clearrifts", _ => ClearRifts());
        Add("move", a => Move(Player(a), (int)a[1], (int)a[2], (int)a[3]), parsers.Word("player"), parsers.Int("dx"), parsers.Int("dy"), parsers.Int("dz"));
        Add("near", a => MoveNear(Player(a), Find((string)a[1])), parsers.Word("player"), parsers.Word("other"));
        Add("farspawn", a => FarSpawn(Player(a), (int)a[1]), parsers.Word("player"), parsers.Int("distance"));
        Add("registry", _ => Registry());
        Add("hurt", a => Hurt(Player(a), (int)a[1]), parsers.Word("player"), parsers.Int("damage"));
        Add("age", a => Age((string)a[0], (int)a[1]), parsers.Word("player"), parsers.Int("hours"));
        Add("checkreturns", _ => CheckReturns());
        Add("privileges", a => Player(a).Privileges, parsers.Word("player"));
    }

    private IServerPlayer Find(string name) =>
        api.World.AllOnlinePlayers.Cast<IServerPlayer>().FirstOrDefault(p => p.PlayerName == name)
        ?? throw new InvalidOperationException("no such online player: " + name);

    private IServerPlayer Player(TextCommandCallingArgs a) => Find((string)a[0]);

    private static object State(IServerPlayer p)
    {
        EntityPlayer e = p.Entity;
        var health = e.WatchedAttributes.GetTreeAttribute("health");
        var hunger = e.WatchedAttributes.GetTreeAttribute("hunger");
        Vec3d spawn = p.GetSpawnPosition(false).XYZ;
        return new
        {
            alive = e.Alive,
            mode = p.WorldData.CurrentGameMode.ToString(),
            noclip = p.WorldData.NoClip,
            deaths = p.WorldData.Deaths,
            lives = e.WatchedAttributes.GetInt("temporalrebirthLivesLeft", -99),
            downed = e.WatchedAttributes.GetBool("downed", false),
            x = e.Pos.X, y = e.Pos.Y, z = e.Pos.Z,
            spawnX = spawn.X, spawnY = spawn.Y, spawnZ = spawn.Z,
            health = health?.GetFloat("currenthealth", 0f), maxHealth = health?.GetFloat("maxhealth", 0f),
            satiety = hunger?.GetFloat("currentsaturation", 0f), maxSatiety = hunger?.GetFloat("maxsaturation", 0f),
            gears = CountGears(p),
        };
    }

    private static int CountGears(IServerPlayer p)
    {
        int count = 0;
        foreach (ItemSlot slot in p.InventoryManager.GetHotbarInventory())
        {
            if (slot.Itemstack?.Collectible is ItemTemporalGear)
            {
                count += slot.StackSize;
            }
        }

        return count;
    }

    /// <summary>Puts one gear in the active slot and finishes holding it for 4 seconds on the block below.</summary>
    private object UseGear(IServerPlayer p)
    {
        ItemSlot slot = p.InventoryManager.ActiveHotbarSlot;
        Item item = api.World.SearchItems(new AssetLocation("game:gear-temporal")).First();
        slot.Itemstack = new ItemStack(item, 1);
        var selection = new BlockSelection { Position = p.Entity.Pos.AsBlockPos.DownCopy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) };
        item.OnHeldInteractStop(4f, slot, p.Entity, selection, null);
        int left = slot.StackSize;
        slot.Itemstack = null;
        slot.MarkDirty();
        return new { consumed = left == 0 };
    }

    private object AddRift(IServerPlayer p, int distance)
    {
        var rifts = api.ModLoader.GetModSystem<ModSystemRifts>();
        double now = api.World.Calendar.TotalHours;
        var rift = new Rift
        {
            RiftId = rifts.NextRiftId,
            Size = 1f,
            Position = p.Entity.Pos.XYZ.AddCopy(distance, 1, 0),
            SpawnedTotalHours = now - 1,
            DieAtTotalHours = now + 1000,
        };
        rifts.riftsById[rift.RiftId] = rift;
        return new { rift.RiftId };
    }

    private object ClearRifts()
    {
        api.ModLoader.GetModSystem<ModSystemRifts>().riftsById.Clear();
        return new { ok = true };
    }

    private static object Move(IServerPlayer p, int dx, int dy, int dz)
    {
        p.Entity.TeleportToDouble(p.Entity.Pos.X + dx, p.Entity.Pos.Y + dy, p.Entity.Pos.Z + dz);
        return new { ok = true };
    }

    private static object MoveNear(IServerPlayer p, IServerPlayer other)
    {
        p.Entity.TeleportToDouble(other.Entity.Pos.X + 1, other.Entity.Pos.Y, other.Entity.Pos.Z);
        return new { ok = true };
    }

    /// <summary>Own spawn point in land nobody visited: going there waits for world generation.</summary>
    private static object FarSpawn(IServerPlayer p, int distance)
    {
        p.SetSpawnPosition(new PlayerSpawnPos((int)p.Entity.Pos.X + distance, 160, (int)p.Entity.Pos.Z));
        return new { ok = true };
    }

    /// <summary>Who the mod thinks is out of lives: uid -> name and phase.</summary>
    private object Registry()
    {
        object rebirth = Rebirth();
        object afterlife = rebirth.GetType().GetProperty("Afterlife")!.GetValue(rebirth)!;
        var players = (System.Collections.IEnumerable)afterlife.GetType().GetProperty("Players")!.GetValue(afterlife)!;
        var answer = new Dictionary<string, object>();
        foreach (object pair in players)
        {
            object value = pair.GetType().GetProperty("Value")!.GetValue(pair)!;
            answer[(string)pair.GetType().GetProperty("Key")!.GetValue(pair)!] = new
            {
                name = (string)value.GetType().GetProperty("Name")!.GetValue(value)!,
                phase = value.GetType().GetProperty("Phase")!.GetValue(value)!.ToString(),
            };
        }

        return answer;
    }

    /// <summary>Real damage, so mods like Downed get their say, unlike the game's /entity cmd kill.</summary>
    private static object Hurt(IServerPlayer p, int damage)
    {
        bool received = p.Entity.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.BluntAttack, IgnoreInvFrames = true }, damage);
        return new { received };
    }

    private object Rebirth()
    {
        ModSystem mod = api.ModLoader.GetModSystem("TemporalRebirth.TemporalRebirthModSystem");
        return mod.GetType().GetProperty("Rebirth", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
    }

    /// <summary>Makes a ghost look as if it died that many hours earlier.</summary>
    private object Age(string name, int hours)
    {
        object rebirth = Rebirth();
        object afterlife = rebirth.GetType().GetProperty("Afterlife")!.GetValue(rebirth)!;
        object ghost = afterlife.GetType().GetMethod("Get")!.Invoke(afterlife, ["bot-" + name]) ?? throw new InvalidOperationException("not a ghost: " + name);
        PropertyInfo died = ghost.GetType().GetProperty("DiedAtUtc")!;
        died.SetValue(ghost, ((DateTime)died.GetValue(ghost)!).AddHours(-hours));
        return new { ok = true };
    }

    private object CheckReturns()
    {
        object rebirth = Rebirth();
        rebirth.GetType().GetMethod("CheckReturns")!.Invoke(rebirth, null);
        return new { ok = true };
    }
}
