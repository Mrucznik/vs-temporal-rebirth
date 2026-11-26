using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalRebirth;

/// <summary>Serving -> Returning -> back among the living, by time, ritual or admin.</summary>
internal class Returns(ICoreServerAPI api, Afterlife afterlife, PlayerControl control, Places places, Leash leash, Messages messages)
{
    private bool Unlimited => LifeRules.UnlimitedLives(api.World);

    /// <summary>Starts the return of someone whose time is up.</summary>
    public void Check(string uid, LostPlayer lost)
    {
        // The world went back to unlimited lives (or the mod disabled itself): nobody should stay out.
        if (Unlimited)
        {
            Begin(uid, lost, new Return { Reason = ReturnReason.Released });
        }
        else if (afterlife.EndUtc(lost) is { } at && DateTime.UtcNow >= at)
        {
            Begin(uid, lost, new Return { Reason = ReturnReason.Time });
        }
    }

    /// <summary>Back by the settings' "where they come back", now or at the next login.</summary>
    public void ByAdmin(string uid, LostPlayer lost, int? lives = null) => Begin(uid, lost, new Return { Reason = ReturnReason.Admin, Lives = lives });

    public void ByRitual(string uid, LostPlayer lost, IServerPlayer caster, Vec3d besideCaster, ReviveAt at) =>
        Begin(uid, lost, new Return
        {
            Reason = ReturnReason.Ritual,
            Helper = caster.PlayerName,
            Destination = at switch
            {
                ReviveAt.Caster => LostPlayer.Pack(besideCaster),
                ReviveAt.Body => lost.DeathPos,
                _ => null,
            },
            ToSpawn = at == ReviveAt.Spawn || (at == ReviveAt.Body && lost.DeathPos == null),
        });

    /// <summary>A ghost that hung in a rift comes back where it is.</summary>
    public void AtRift(string uid, LostPlayer lost) => Begin(uid, lost, new Return { Reason = ReturnReason.Ritual });

    private void Begin(string uid, LostPlayer lost, Return how)
    {
        if (lost.Phase == Phase.Returning)
        {
            return;
        }

        bool entered = lost.Phase == Phase.Serving;
        lost.Phase = Phase.Returning;
        lost.Return = how;
        afterlife.Save();

        if (control.Online(uid) is not { } player)
        {
            return;
        }

        RebirthConfig rules = afterlife.Rules(lost);
        if (how.Reason == ReturnReason.Time && entered && rules.DieAtEnd && rules.Walks && player.Entity.Alive)
        {
            // Belongings drop by the world's rules; OnPlayerDeath then continues the journey.
            player.Entity.Die(EnumDespawnReason.Death, new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Injury });
            return;
        }

        Travel(player, lost);
    }

    /// <summary>Sets off, or sets off again after a relog.</summary>
    public void Travel(IServerPlayer player, LostPlayer lost)
    {
        player.Entity.TryUnmount();
        ClientState.Clear(player.Entity);
        if (!PlayerControl.Revive(player.Entity))
        {
            // Went down while out of lives; that must not cost the life they come back with.
            control.StandUp(player);
        }

        // Same order as vanilla respawn: wait for the chunks at the destination and only then become mortal.
        control.MakeSpectator(player, afterlife.Rules(lost).Form == Form.Spectator);
        string uid = player.PlayerUID;
        control.GoTo(player, places.DestinationOf(player, lost), () => Arrive(uid));
    }

    private void Arrive(string uid)
    {
        if (control.Online(uid) is not { } player || afterlife.Get(uid) is not { Phase: Phase.Returning } lost)
        {
            return;
        }

        RebirthConfig rules = afterlife.Rules(lost);
        EntityPlayer entity = player.Entity;
        PlayerControl.Revive(entity);
        if (!Unlimited)
        {
            int lives = lost.Return?.Lives ?? rules.ReturnLives;
            LifeRules.SetLivesLeft(player, lives < 0 ? Math.Max(1, LifeRules.WorldLives(api.World)) : lives);
        }

        afterlife.Remove(uid);
        leash.Forget(uid);
        ClientState.Clear(entity);
        LifeRules.Sync(player);
        control.MakeMortal(player);
        PlayerControl.SetVital(entity, "health", "currenthealth", "maxhealth", rules.HealthPercent, 1f);
        PlayerControl.SetVital(entity, "hunger", "currentsaturation", "maxsaturation", rules.SatietyPercent, 0f);
        Announce(player, lost.Return);
        if (lost.Return?.Reason == ReturnReason.Ritual)
        {
            TemporalRebirthModSystem.Server?.Rituals.Face(player, lost.Return.Helper);
        }
    }

    private void Announce(IServerPlayer player, Return? how)
    {
        switch (how?.Reason)
        {
            case ReturnReason.Ritual when how.Helper == null:
                messages.TellText(player, "rift-selfback");
                break;
            case ReturnReason.Ritual:
                messages.Tell(player, "RevivedRitual", ("caster", how.Helper ?? ""), ("player", player.PlayerName));
                return;
            case ReturnReason.Admin:
                messages.Tell(player, "RevivedAdmin", ("player", player.PlayerName));
                break;
            default:
                messages.Tell(player, "Returned", ("player", player.PlayerName));
                break;
        }

        messages.TellEveryone("AnnounceReturned", ("player", player.PlayerName));
    }
}
