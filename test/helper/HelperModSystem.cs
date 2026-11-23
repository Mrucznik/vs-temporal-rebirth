using System.Reflection;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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
        Add("riftnear", a => RiftNear(Find((string)a[0]), (int)a[1], (int)a[2], (int)a[3], (int)a[4]), parsers.Word("player"), parsers.Int("dx"), parsers.Int("dy"), parsers.Int("dz"), parsers.Int("size"));
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
        Add("players", _ => api.World.AllOnlinePlayers.Cast<IServerPlayer>().Where(p => p.ConnectionState is EnumClientState.Connected or EnumClientState.Playing && p.Entity != null).Select(p => p.PlayerName).ToArray());
        Add("put", a => Put(Player(a), Find((string)a[1]), (int)a[2], (int)a[3], (int)a[4]), parsers.Word("player"), parsers.Word("other"), parsers.Int("dx"), parsers.Int("above the ground"), parsers.Int("dz"));
        Add("box", a => Box(Player(a), (string)a[1], (int)a[2], (int)a[3], (int)a[4], (int)a[5], (int)a[6], (int)a[7]), parsers.Word("player"), parsers.Word("block"), parsers.Int("x1"), parsers.Int("y1"), parsers.Int("z1"), parsers.Int("x2"), parsers.Int("y2"), parsers.Int("z2"));
        Add("view", a => View(Player(a)), parsers.Word("player"));
        Add("front", a => Front(Player(a), Find((string)a[1]), (int)a[2]), parsers.Word("player"), parsers.Word("other"), parsers.Int("distance"));
        Add("turn", a => Turn(Player(a), (int)a[1]), parsers.Word("player"), parsers.Int("degrees"));
        Add("meadow", a => Meadow(Player(a)), parsers.Word("player"));
        Add("glide", a => Glide(Player(a), (int)a[1], (int)a[2], (int)a[3], (int)a[4]), parsers.Word("player"), parsers.Int("dx"), parsers.Int("dy"), parsers.Int("dz"), parsers.Int("milliseconds"));
        Add("dress", a => Dress(Player(a), (int)a[1]), parsers.Word("player"), parsers.Int("look"));
        Add("wear", a => Wear(Player(a), (string)a[1]), parsers.Word("player"), parsers.Word("item"));
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

    /// <summary>Scenes: a grown rift next to someone, sent to clients at once (the game itself sends rifts every 3 s).</summary>
    private object RiftNear(IServerPlayer p, int dx, int dy, int dz, int size)
    {
        var rifts = api.ModLoader.GetModSystem<ModSystemRifts>();
        double now = api.World.Calendar.TotalHours;
        var rift = new Rift
        {
            RiftId = rifts.NextRiftId,
            Size = size,
            Position = p.Entity.Pos.XYZ.AddCopy(dx, dy, dz),
            SpawnedTotalHours = now - 2,
            DieAtTotalHours = now + 1000,
        };
        rifts.riftsById[rift.RiftId] = rift;
        rifts.BroadCastRifts();
        return new { rift.RiftId };
    }

    private object ClearRifts()
    {
        var rifts = api.ModLoader.GetModSystem<ModSystemRifts>();
        rifts.riftsById.Clear();
        rifts.BroadCastRifts();
        return new { ok = true };
    }

    /// <summary>Teleports wait for the land; "ARRIVED name" in the log says when it is done.</summary>
    private object Move(IServerPlayer p, int dx, int dy, int dz)
    {
        string name = p.PlayerName;
        p.Entity.TeleportToDouble(p.Entity.Pos.X + dx, p.Entity.Pos.Y + dy, p.Entity.Pos.Z + dz, () => api.Logger.Notification("ARRIVED {0}", name));
        return new { ok = true };
    }

    private object MoveNear(IServerPlayer p, IServerPlayer other)
    {
        string name = p.PlayerName;
        p.Entity.TeleportToDouble(other.Entity.Pos.X + 1, other.Entity.Pos.Y, other.Entity.Pos.Z, () => api.Logger.Notification("ARRIVED {0}", name));
        return new { ok = true };
    }

    /// <summary>Scenes: puts a player on the ground at a distance from another one, in land that is loaded.</summary>
    private object Put(IServerPlayer p, IServerPlayer other, int dx, int above, int dz)
    {
        BlockPos pos = OnGround(other.Entity.Pos.AsBlockPos.AddCopy(dx, 0, dz));
        string name = p.PlayerName;
        p.Entity.TeleportToDouble(pos.X + 0.5, pos.Y + above, pos.Z + 0.5, () => api.Logger.Notification("ARRIVED {0}", name));
        return new { x = pos.X, y = pos.Y + above, z = pos.Z };
    }

    private BlockPos OnGround(BlockPos pos)
    {
        IBlockAccessor blocks = api.World.BlockAccessor;
        pos.Y = blocks.GetTerrainMapheightAt(pos) + 1;
        bool Free(BlockPos at) => blocks.GetBlock(at).GetCollisionBoxes(blocks, at) is not { Length: > 0 };
        while (!Free(pos) || !Free(pos.UpCopy()))
        {
            pos.Up();
        }

        return pos;
    }

    /// <summary>Scenes: where a player looks, which hotbar slot they hold, how bright the day is.</summary>
    private object View(IServerPlayer p) => new
    {
        yaw = p.Entity.Pos.Yaw,
        pitch = p.Entity.Pos.Pitch,
        slot = p.InventoryManager.ActiveHotbarSlotNumber,
        daylight = api.World.Calendar.GetDayLightStrength(p.Entity.Pos.AsBlockPos),
        hour = api.World.Calendar.HourOfDay,
        chosen = p.GetModData("createCharacter", false),
    };

    /// <summary>Scenes: puts a player on the ground where another one looks, face to face.</summary>
    private object Front(IServerPlayer p, IServerPlayer other, int distance)
    {
        // The view vector of the API points backwards for a player's yaw (checked on a real client).
        Vec3f ahead = EntityPos.GetViewVector(0, other.Entity.Pos.Yaw) * -1;
        BlockPos pos = OnGround(other.Entity.Pos.AsBlockPos.AddCopy((int)Math.Round(ahead.X * distance), 0, (int)Math.Round(ahead.Z * distance)));
        string name = p.PlayerName;
        EntityPos to = p.Entity.Pos.Copy().SetPos(pos.X + 0.5, pos.Y, pos.Z + 0.5);
        to.Yaw = other.Entity.Pos.Yaw + GameMath.PI;
        p.Entity.BodyYaw = to.Yaw;
        p.Entity.TeleportTo(to, () => api.Logger.Notification("ARRIVED {0}", name));
        return new { x = pos.X, y = pos.Y, z = pos.Z };
    }

    /// <summary>Scenes: turns a bot's body and head; how a client draws a yaw is found by looking.</summary>
    private object Turn(IServerPlayer p, int degrees)
    {
        // A bot sends no positions, so the yaw is set where the server sends it from.
        float yaw = degrees * GameMath.DEG2RAD;
        EntityPlayer e = p.Entity;
        e.ServerPos.Yaw = e.Pos.Yaw = e.BodyYaw = yaw;
        e.ServerPos.HeadYaw = e.Pos.HeadYaw = 0;
        api.Logger.Notification("ARRIVED {0}", p.PlayerName);
        return new { yaw };
    }

    /// <summary>Scenes: moves a player to the flattest dry spot without trees in the land loaded around them.</summary>
    private object Meadow(IServerPlayer p)
    {
        const int Search = 96, Half = 8;
        IBlockAccessor blocks = api.World.BlockAccessor;
        BlockPos at = p.Entity.Pos.AsBlockPos, best = at.Copy();
        int bestRange = int.MaxValue;
        for (int x = -Search; x <= Search; x += 4)
        {
            for (int z = -Search; z <= Search; z += 4)
            {
                int low = int.MaxValue, high = int.MinValue;
                for (int dx = -Half; dx <= Half && low != -1; dx += 2)
                {
                    for (int dz = -Half; dz <= Half; dz += 2)
                    {
                        BlockPos pos = at.AddCopy(x + dx, 0, z + dz);
                        int height = blocks.GetChunkAtBlockPos(pos.X, at.Y, pos.Z) == null ? -1 : blocks.GetTerrainMapheightAt(pos);
                        pos.Y = height + 1;
                        // Water, or a tree above.
                        if (height < 0 || blocks.GetBlock(pos, BlockLayersAccess.Fluid).IsLiquid() || blocks.GetRainMapHeightAt(pos) > height + 1)
                        {
                            low = -1;
                            break;
                        }

                        low = Math.Min(low, height);
                        high = Math.Max(high, height);
                    }
                }

                if (low >= 0 && high - low < bestRange)
                {
                    bestRange = high - low;
                    best = at.AddCopy(x, 0, z);
                }
            }
        }

        if (bestRange < int.MaxValue)
        {
            string name = p.PlayerName;
            BlockPos ground = OnGround(best);
            p.Entity.TeleportToDouble(ground.X + 0.5, ground.Y, ground.Z + 0.5, () => api.Logger.Notification("ARRIVED {0}", name));
        }

        return new { found = bestRange < int.MaxValue, range = bestRange, x = best.X - at.X, z = best.Z - at.Z };
    }

    /// <summary>Scenes: fills a box given relative to the block a player stands in.</summary>
    private object Box(IServerPlayer p, string code, int x1, int y1, int z1, int x2, int y2, int z2)
    {
        Block block = api.World.GetBlock(new AssetLocation(code)) ?? throw new InvalidOperationException("no such block: " + code);
        BlockPos at = p.Entity.Pos.AsBlockPos;
        for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
        {
            for (int y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
            {
                for (int z = Math.Min(z1, z2); z <= Math.Max(z1, z2); z++)
                {
                    BlockPos pos = at.AddCopy(x, y, z);
                    api.World.BlockAccessor.SetBlock(0, pos);
                    api.World.BlockAccessor.SetBlock(0, pos, BlockLayersAccess.Fluid);
                    api.World.BlockAccessor.SetBlock(block.BlockId, pos, block.ForFluidsLayer ? BlockLayersAccess.Fluid : BlockLayersAccess.Default);
                }
            }
        }

        return new { ok = true };
    }

    /// <summary>
    /// Scenes: moves a player smoothly by an offset over a time, in steps of 50 ms with easing; "GLIDED name" in the log
    /// when done. A ghost (spectator) floats where it is put, no gravity.
    /// </summary>
    private object Glide(IServerPlayer p, int dx, int dy, int dz, int ms)
    {
        EntityPlayer e = p.Entity;
        Vec3d from = e.ServerPos.XYZ, to = from.AddCopy(dx, dy, dz);
        string name = p.PlayerName;
        long started = api.World.ElapsedMilliseconds, id = 0;
        id = api.Event.RegisterGameTickListener(_ =>
        {
            double t = Math.Min(1.0, (api.World.ElapsedMilliseconds - started) / (double)Math.Max(1, ms));
            double k = t * t * (3 - 2 * t);
            double x = from.X + ((to.X - from.X) * k), y = from.Y + ((to.Y - from.Y) * k), z = from.Z + ((to.Z - from.Z) * k);
            // Setting the server's position is not enough: the server relays a player's position from that player's own
            // client, and a bot sends none. Small teleports reach every client.
            e.TeleportToDouble(x, y, z);
            if (t >= 1)
            {
                api.Event.UnregisterGameTickListener(id);
                api.Logger.Notification("GLIDED {0}", name);
            }
        }, 50);
        return new { x = to.X, y = to.Y, z = to.Z };
    }

    /// <summary>
    /// Scenes: a bot never picks a character, and a player without one has no body on other screens. Picks one from the
    /// standard variants (the number shifts the choice) and the first class, with its clothes.
    /// </summary>
    private object Dress(IServerPlayer p, int look)
    {
        EntityPlayer entity = p.Entity;
        var skin = entity.GetBehavior<EntityBehaviorExtraSkinnable>();
        foreach (SkinnablePart part in skin.AvailableSkinParts)
        {
            SkinnablePartVariant[] standard = part.Variants.Where(v => v.Category == "standard").ToArray();
            if (standard.Length == 0)
            {
                continue;
            }

            string code = part.Code is "mustache" or "beard" ? "none" : standard[(look * 7 + part.Code.Length) % standard.Length].Code;
            skin.selectSkinPart(part.Code, code, retesselateShape: false, playVoice: false);
        }

        var characters = api.ModLoader.GetModSystem<CharacterSystem>();
        characters.setCharacterClass(entity, characters.characterClasses[0].Code, true);
        p.SetModData("createCharacter", true);
        entity.WatchedAttributes.MarkPathDirty("skinConfig");
        p.BroadcastPlayerData(true);
        return new { characterClass = characters.characterClasses[0].Code };
    }

    /// <summary>Scenes: puts an item where it is worn, else into the first hotbar slot.</summary>
    private object Wear(IServerPlayer p, string code)
    {
        var name = new AssetLocation(code);
        CollectibleObject thing = (CollectibleObject?)api.World.GetItem(name) ?? api.World.GetBlock(name) ?? throw new InvalidOperationException("no such item: " + code);
        var stack = new ItemStack(thing);
        var source = new DummySlot(stack);
        ItemSlot slot = p.InventoryManager.GetOwnInventory("character").FirstOrDefault(s => s.CanHold(source)) ?? p.InventoryManager.GetHotbarInventory()[0];
        slot.Itemstack = stack;
        slot.MarkDirty();
        return new { inventory = slot.Inventory.ClassName, item = thing.Code.ToString() };
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
