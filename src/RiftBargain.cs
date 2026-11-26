using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalRebirth;

/// <summary>
/// What rifts take and give back (Ritual.HealthCost, the Don't Starve preset): a revival without a gear costs the revived
/// player maximum health for good, a gear offered to a rift gives it back, and a ghost that hangs in a rift comes back alone.
/// The loss is a stat of the player's entity (maxhealthExtraPoints), so the game keeps and applies it like a trait.
/// </summary>
internal class RiftBargain(ICoreServerAPI api, RebirthConfig config, Afterlife afterlife, Returns returns, Messages messages)
{
    private const string StatCode = "temporalrebirth";

    /// <summary>How long a ghost must hang in a rift, and how near its middle that is.</summary>
    internal const float SelfSeconds = 5f;
    internal const double SelfReach = 2.5;

    private readonly Dictionary<string, float> inRift = new();
    private readonly HashSet<string> toldTooWeak = new();

    public void Start() => api.Event.RegisterGameTickListener(SelfAtRift, 250);

    // ---------- health ----------

    /// <summary>Maximum health this player has given to rifts.</summary>
    internal static int Lost(EntityPlayer entity) => entity.WatchedAttributes.GetInt(ClientState.HealthLost);

    internal static void SetLost(EntityPlayer entity, int lost)
    {
        lost = Math.Max(0, lost);
        if (lost == 0)
        {
            entity.WatchedAttributes.RemoveAttribute(ClientState.HealthLost);
        }
        else
        {
            entity.WatchedAttributes.SetInt(ClientState.HealthLost, lost);
        }

        entity.WatchedAttributes.MarkPathDirty(ClientState.HealthLost);
        if (lost == 0)
        {
            entity.Stats.Remove("maxhealthExtraPoints", StatCode);
        }
        else
        {
            entity.Stats.Set("maxhealthExtraPoints", StatCode, -lost, true);
        }

        entity.GetBehavior<EntityBehaviorHealth>()?.MarkDirty();
    }

    /// <summary>Whether a revival for health keeps this player at or above the least health allowed.</summary>
    public bool CanPay(EntityPlayer entity) =>
        config.RitualHealthCost > 0 && entity.GetBehavior<EntityBehaviorHealth>() is { } health
        && health.MaxHealth - config.RitualHealthCost >= config.RitualHealthMin;

    public void Charge(EntityPlayer entity) => SetLost(entity, Lost(entity) + config.RitualHealthCost);

    /// <summary>What a gear offered to a rift gives back now, 0 when nothing.</summary>
    public int OfferNow(IServerPlayer player) =>
        config.RitualHealthCost > 0 && player.Entity != null && Lost(player.Entity) > 0
        && Rifts.IsNearActiveRift(api.World, player.Entity.Pos.XYZ, config.RitualRange)
            ? Math.Min(Lost(player.Entity), config.RitualHealthCost) : 0;

    /// <summary>Takes the gear and gives health back. False when there is nothing to give.</summary>
    public bool Offer(IServerPlayer player, ItemSlot slot)
    {
        int back = OfferNow(player);
        if (back <= 0)
        {
            return false;
        }

        SetLost(player.Entity, Lost(player.Entity) - back);
        slot.TakeOut(1);
        slot.MarkDirty();
        Vec3d at = player.Entity.Pos.XYZ;
        api.World.PlaySoundAt(new AssetLocation("sounds/effect/translocate-breakdimension.ogg"), at.X, at.Y, at.Z, null, false, 32f);
        api.World.SpawnParticles(RitualLook.Burst(at.AddCopy(0, 1, 0)));
        messages.TellText(player, "rift-offered", back);
        api.Logger.ModAudit("{0} offered a gear to a rift and got {1} health back", player.PlayerName, back);
        return true;
    }

    // ---------- a ghost alone at a rift ----------

    private void SelfAtRift(float dt)
    {
        if (!config.RitualSelfAtRift || LifeRules.UnlimitedLives(api.World))
        {
            if (inRift.Count > 0)
            {
                foreach (string uid in inRift.Keys.ToList())
                {
                    Reset(uid);
                }
            }

            return;
        }

        foreach (var (uid, lost) in afterlife.Players.ToList())
        {
            if (lost.Phase != Phase.Serving || afterlife.Rules(lost).Form == Form.Ban
                || api.World.PlayerByUid(uid) is not IServerPlayer { ConnectionState: EnumClientState.Playing, Entity: { Alive: true } entity } player
                || Rifts.NearestActive(api.World, entity.Pos.XYZ.Add(0, 0.9, 0), SelfReach) == null)
            {
                Reset(uid);
                continue;
            }

            if (config.RitualHealthCost > 0 && !CanPay(entity))
            {
                if (toldTooWeak.Add(uid))
                {
                    messages.TellText(player, "rift-tooweak");
                }

                Reset(uid, keepTold: true);
                continue;
            }

            float held = inRift.GetValueOrDefault(uid) + dt;
            inRift[uid] = held;
            ClientState.Set(entity, ClientState.SelfRift, (int)Math.Min(100, held / SelfSeconds * 100));
            if (held < SelfSeconds)
            {
                continue;
            }

            Reset(uid);
            if (config.RitualHealthCost > 0)
            {
                Charge(entity);
            }

            Vec3d at = entity.Pos.XYZ;
            api.World.PlaySoundAt(new AssetLocation("sounds/effect/translocate-breakdimension.ogg"), at.X, at.Y, at.Z, null, false, 48f);
            api.World.SpawnParticles(RitualLook.Burst(at.AddCopy(0, 1, 0)));
            api.Logger.ModAudit("{0} came back alone at a rift", lost.Name);
            returns.AtRift(uid, lost);
        }
    }

    private void Reset(string uid, bool keepTold = false)
    {
        if (!keepTold)
        {
            toldTooWeak.Remove(uid);
        }

        if (inRift.Remove(uid) && api.World.PlayerByUid(uid)?.Entity is { } entity)
        {
            ClientState.Set(entity, ClientState.SelfRift, (int?)null);
        }
    }
}
