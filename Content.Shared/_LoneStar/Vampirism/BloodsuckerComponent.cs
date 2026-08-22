using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Lonestar.Vampirism;

[RegisterComponent, NetworkedComponent]
public sealed partial class BloodsuckerComponent : Component
{
    [DataField] public EntProtoId Action = "ActionSuckBlood";

    [DataField] public EntityUid? ActionEntity;

    [DataField("amount")] public int Amount = 15;

    [DataField("biteSound")] public SoundSpecifier BiteSound = new SoundPathSpecifier("/Audio/Effects/bite.ogg");

    [DataField("sound")] public SoundSpecifier SuckSound = new SoundPathSpecifier("/Audio/Items/drink.ogg");
}

public sealed partial class SuckBloodEvent : EntityTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class SuckBloodDoAfterEvent : SimpleDoAfterEvent;
