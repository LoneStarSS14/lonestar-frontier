using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Goobstation.SlotMachine.ClawGame;

/// <summary>
/// This is used for the claw game machine.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ClawMachineComponent : Component
{
    [DataField, AutoNetworkedField]
    public float DoAfterTime = 3.9f;

    [DataField]
    public SoundSpecifier PlaySound = new SoundCollectionSpecifier("Keyboard");

    [DataField]
    public SoundSpecifier LoseSound = new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg");

    [DataField]
    public SoundSpecifier WinSound = new SoundPathSpecifier("/Audio/Effects/Arcade/win.ogg");

    [DataField, AutoNetworkedField]
    public float WinChance = .10f; // LoneStar, cant set limit like arcade machines.

    [DataField, AutoNetworkedField]
    public bool IsSpinning;

    [DataField, AutoNetworkedField]
    public List<EntProtoId>? Rewards;

    [DataField, AutoNetworkedField]
    public List<EntProtoId>? EvilRewards;

    [DataField, AutoNetworkedField]
    public bool Emagged;
}

[Serializable, NetSerializable]
public enum ClawMachineVisuals : byte
{
    Spinning,
    NormalSprite
}
