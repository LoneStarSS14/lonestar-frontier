using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._HL.Drugs;

/// <summary>
/// Component that allows packets to be sealed by right-clicking when they contain reagents
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PacketSealingComponent : Component
{
    [ViewVariables, DataField, AutoNetworkedField]
    public PacketState State = PacketState.Open;

    /// <summary>
    /// Time it takes to seal a packet
    /// </summary>
    [DataField, ViewVariables]
    public TimeSpan SealDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Sound played when sealing a packet
    /// </summary>
    [DataField, ViewVariables]
    public SoundPathSpecifier? SealSound = new SoundPathSpecifier("/Audio/Effects/packetrip.ogg");

    /// <summary>
    /// The solution container name to check for reagents
    /// </summary>
    [DataField]
    public string SolutionName = "drink";
}

[Serializable, NetSerializable]
public enum PacketState : byte
{
    Open,
    Sealed
}
