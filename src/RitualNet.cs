using ProtoBuf;

namespace TemporalRebirth;

/// <summary>
/// What a temporal gear held here would do, as the caster's client should show it. Worked out by the server, which
/// stays the judge: the client only uses it for the hint, the look of the hold and to refuse a hold that cannot work.
/// </summary>
public enum RitualState
{
    /// <summary>Nobody is waiting, or rituals are off: the gear is the vanilla gear.</summary>
    None,
    Ready,
    NobodyRevivable,
    Cooldown,
    TooFarRift,
    TooFarPlace,
    TooFarPlayer,

    /// <summary>The caster is out of lives, downed, or in a game mode that cannot use a gear.</summary>
    CannotCast,

    /// <summary>Nobody to bring back, but a rift is near and the caster gave it health: the gear buys it back.</summary>
    Offer,
}

internal static class RitualNet
{
    internal const string Channel = "temporalrebirth-ritual";

    /// <summary>How long the chooser stays open, and how far the caster may step away from where the hold ended.</summary>
    internal const int ChooserSeconds = 30;
    internal const double ChooserMoveLimit = 3;
}

// Packets are public: protobuf-net is only sure to build serializers for public types.
[ProtoContract]
public sealed class RitualHintPacket
{
    [ProtoMember(1)] public RitualState State { get; set; }

    /// <summary>The gear does not set the spawn point here: holding it is a ritual or nothing.</summary>
    [ProtoMember(2)] public bool AtSite { get; set; }

    /// <summary>Ready: how many can be brought back now.</summary>
    [ProtoMember(3)] public int Count { get; set; }

    /// <summary>Cooldown: seconds until the first one can be brought back.</summary>
    [ProtoMember(4)] public int SecondsLeft { get; set; }

    /// <summary>Ready next to ghosts: the one looked at is brought back.</summary>
    [ProtoMember(5)] public bool LookAtGhost { get; set; }

    /// <summary>Offer: how much health a gear gives back. Otherwise how much a revival without a gear costs, when one is possible.</summary>
    [ProtoMember(6)] public int Health { get; set; }

    /// <summary>A revival without a gear is possible here: holding the right button at the rift, the gear not in hand.</summary>
    [ProtoMember(7)] public bool ByHand { get; set; }

    /// <summary>The seconds are compared by the sender, which knows how long ago the other one went out.</summary>
    public bool SameAs(RitualHintPacket? other) => other != null && other.State == State && other.AtSite == AtSite && other.Count == Count && other.LookAtGhost == LookAtGhost && other.Health == Health && other.ByHand == ByHand;
}

[ProtoContract]
public sealed class ChooserEntry
{
    [ProtoMember(1)] public string Uid { get; set; } = "";
    [ProtoMember(2)] public string Name { get; set; } = "";
    /// <summary>A <see cref="TemporalRebirth.Form"/>.</summary>
    [ProtoMember(3)] public int Form { get; set; }

    /// <summary>-1: only a ritual or an admin brings them back.</summary>
    [ProtoMember(4)] public int SecondsLeft { get; set; } = -1;
    [ProtoMember(5)] public bool Online { get; set; }
}

[ProtoContract]
public sealed class ChooserOpenPacket
{
    [ProtoMember(1)] public List<ChooserEntry> Entries { get; set; } = new();

    /// <summary>Ritual.Where Anywhere: the gear may still bind the caster to this place instead, as in vanilla.</summary>
    [ProtoMember(2)] public bool AllowBind { get; set; }

    /// <summary>Where the caster stood when the hold ended; stepping away closes the chooser.</summary>
    [ProtoMember(3)] public double X { get; set; }
    [ProtoMember(4)] public double Y { get; set; }
    [ProtoMember(5)] public double Z { get; set; }
}

[ProtoContract]
public sealed class ChooserClosePacket
{
    /// <summary>Set when sending: an empty message arrives as null.</summary>
    [ProtoMember(1)] public bool Unused { get; set; }
}

[ProtoContract]
public sealed class ChooserPickPacket
{
    /// <summary>Whom to bring back; empty with <see cref="Bind"/> or <see cref="Cancel"/>.</summary>
    [ProtoMember(1)] public string Uid { get; set; } = "";
    [ProtoMember(2)] public bool Bind { get; set; }
    [ProtoMember(3)] public bool Cancel { get; set; }
}

/// <summary>To a player just brought back by a ritual: turn to the one who did it.</summary>
[ProtoContract]
public sealed class FacePacket
{
    [ProtoMember(1)] public double X { get; set; }
    [ProtoMember(2)] public double Y { get; set; }
    [ProtoMember(3)] public double Z { get; set; }
}

/// <summary>A hold without a gear at a rift (Ritual.HealthCost): begun, let go, or held long enough.</summary>
[ProtoContract]
public sealed class HandHoldPacket
{
    /// <summary>0 begun, 1 let go, 2 held long enough.</summary>
    [ProtoMember(1)] public int Step { get; set; }
}

