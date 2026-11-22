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
        Add("claim", a => Claim(Player(a), (string)a[1], (int)a[2], (bool)a[3]), parsers.Word("player"), parsers.Word("group"), parsers.Int("size"), parsers.Bool("everyone may use"));
        Add("unclaim", _ => Unclaim());
        Add("access", a => Access(Player(a), (int)a[1]), parsers.Word("player"), parsers.Int("dx"));
        Add("tracks", a => Tracks(Player(a), Find((string)a[1])), parsers.Word("player"), parsers.Word("other"));
        Add("surround", a => Surround(Player(a), (string)a[1], (int)a[2]), parsers.Word("player"), parsers.Word("block"), parsers.Int("radius"));
        Add("pointfar", a => PointFar(Player(a), (string)a[1], (int)a[2]), parsers.Word("player"), parsers.Word("place"), parsers.Int("distance"));
        Add("interact", a => Interact(Player(a), Find((string)a[1]), (string)a[2], (int)a[3]), parsers.Word("player"), parsers.Word("other"), parsers.Word("item or none"), parsers.Int("mouse button"));
        Add("oldentry", a => OldEntry((string)a[0]), parsers.Word("player"));
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
            liquid = e.World.BlockAccessor.GetBlock(e.Pos.AsBlockPos, BlockLayersAccess.Fluid).IsLiquid(),
            mayRespawn = e.WatchedAttributes.GetBool("temporalrebirthMayRespawn"),
            form = e.WatchedAttributes.GetString("temporalrebirthForm"),
            look = e.WatchedAttributes.GetString("temporalrebirthGhostLook"),
            seconds = e.WatchedAttributes.GetInt("temporalrebirthSecondsLeft", -1),
            timerText = e.WatchedAttributes.GetString("temporalrebirthTimerText"),
            leash = e.WatchedAttributes.GetString("temporalrebirthLeash"),
            zoneAccess = e.WatchedAttributes.GetString("temporalrebirthZoneAccess"),
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

    /// <summary>Teleports wait for the land; "ARRIVED name" in the log says when it is done.</summary>
    private object Move(IServerPlayer p, int dx, int dy, int dz)
    {
        string name = p.PlayerName;
        p.Entity.TeleportToDouble(p.Entity.Pos.X + dx, p.Entity.Pos.Y + dy, p.Entity.Pos.Z + dz, () => api.Logger.Notification("ARRIVED {0}", name));
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
        object afterlife = Part("Afterlife");
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

    /// <summary>A claim of someone else, full height, around a player and shared with a group that is made if needed.</summary>
    private object Claim(IServerPlayer p, string group, int size, bool everyone)
    {
        if (api.Groups.GetPlayerGroupByName(group) is not { } shared)
        {
            api.Groups.AddPlayerGroup(shared = new PlayerGroup { Name = group, OwnerUID = "someone" });
        }

        BlockPos at = p.Entity.Pos.AsBlockPos;
        LandClaim claim = LandClaim.CreateClaim("someone", 0);
        claim.OwnedByPlayerUid = "someone";
        claim.AllowUseEveryone = everyone;
        claim.Areas.Add(new Cuboidi(at.X - size, 0, at.Z - size, at.X + size, api.WorldManager.MapSizeY, at.Z + size));
        claim.PermittedPlayerGroupIds[shared.Uid] = EnumBlockAccessFlags.Traverse;
        api.World.Claims.Add(claim);
        return new { group = shared.Uid };
    }

    /// <summary>Puts a block (lava, for example) around a player, from two below the feet to head height.</summary>
    private object Surround(IServerPlayer p, string code, int radius)
    {
        Block block = api.World.GetBlock(new AssetLocation(code)) ?? throw new InvalidOperationException("no such block: " + code);
        BlockPos at = p.Entity.Pos.AsBlockPos;
        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -2; y <= 1; y++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    BlockPos pos = at.AddCopy(x, y, z);
                    api.World.BlockAccessor.SetBlock(0, pos);
                    api.World.BlockAccessor.SetBlock(block.BlockId, pos, block.ForFluidsLayer ? BlockLayersAccess.Fluid : BlockLayersAccess.Default);
                }
            }
        }

        return new { ok = true };
    }

    /// <summary>Marks a place in land nobody visited.</summary>
    private object PointFar(IServerPlayer p, string name, int distance)
    {
        object afterlife = Part("Afterlife");
        Vec3d pos = new(p.Entity.Pos.X + distance, 160, p.Entity.Pos.Z);
        afterlife.GetType().GetMethod("SetPoint")!.Invoke(afterlife, [name, pos]);
        return new { x = pos.X, y = pos.Y, z = pos.Z };
    }

    /// <summary>What the server does with a click on another player, up to the entity itself.</summary>
    private object Interact(IServerPlayer p, IServerPlayer other, string item, int button)
    {
        ItemSlot slot = p.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = item == "none" ? null : new ItemStack(api.World.GetItem(new AssetLocation(item)) ?? throw new InvalidOperationException("no such item: " + item));
        EnumHandling handling = EnumHandling.PassThrough;
        ((Vintagestory.Server.ServerMain)api.World).EventManager.TriggerPlayerInteractEntity(other.Entity, p, slot, new Vec3d(), button, ref handling);
        slot.Itemstack = null;
        return new { allowed = handling == EnumHandling.PassThrough };
    }

    /// <summary>Makes an entry look as if 0.6.x had written it.</summary>
    private object OldEntry(string name)
    {
        object afterlife = Part("Afterlife");
        object lost = afterlife.GetType().GetMethod("Get")!.Invoke(afterlife, ["bot-" + name])!;
        lost.GetType().GetProperty("Settings")!.SetValue(lost, null);
        afterlife.GetType().GetMethod("Save")!.Invoke(afterlife, null);
        return new { ok = true };
    }

    private object Unclaim()
    {
        foreach (LandClaim claim in api.World.Claims.All.ToList())
        {
            api.World.Claims.Remove(claim);
        }

        return new { left = api.World.Claims.All.Count };
    }

    private object Access(IServerPlayer p, int dx)
    {
        BlockPos pos = p.Entity.Pos.AsBlockPos.AddCopy(dx, -1, 0);
        return new
        {
            use = api.World.Claims.TestAccess(p, pos, EnumBlockAccessFlags.Use).ToString(),
            build = api.World.Claims.TestAccess(p, pos, EnumBlockAccessFlags.BuildOrBreak).ToString(),
        };
    }

    /// <summary>Whether the server sends one player the position of another.</summary>
    private object Tracks(IServerPlayer p, IServerPlayer other)
    {
        var client = ((Vintagestory.Server.ServerMain)api.World).Clients.Values.First(c => c.Player == p);
        return new { tracked = client.TrackedEntities.Contains(other.Entity.EntityId) };
    }

    /// <summary>One of the mod's ServerParts.</summary>
    private object Part(string name)
    {
        ModSystem mod = api.ModLoader.GetModSystem("TemporalRebirth.TemporalRebirthModSystem");
        object server = mod.GetType().GetProperty("Server", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        return server.GetType().GetProperty(name)!.GetValue(server)!;
    }

    /// <summary>Makes a ghost look as if it died that many hours earlier.</summary>
    private object Age(string name, int hours)
    {
        object afterlife = Part("Afterlife");
        object ghost = afterlife.GetType().GetMethod("Get")!.Invoke(afterlife, ["bot-" + name]) ?? throw new InvalidOperationException("not a ghost: " + name);
        PropertyInfo died = ghost.GetType().GetProperty("DiedAtUtc")!;
        died.SetValue(ghost, ((DateTime)died.GetValue(ghost)!).AddHours(-hours));
        return new { ok = true };
    }

    private object CheckReturns()
    {
        object rebirth = Part("Rebirth");
        rebirth.GetType().GetMethod("CheckReturns")!.Invoke(rebirth, null);
        return new { ok = true };
    }
}
