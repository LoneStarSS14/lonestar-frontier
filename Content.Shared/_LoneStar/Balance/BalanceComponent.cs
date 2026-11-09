using System.Numerics;
using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;

namespace Content.Shared._LoneStar.Balance;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class BalanceComponent : Component
{
    // Not MaxBalance, so equipping imbalancing clothing does not refill to full.
    [DataField, AutoNetworkedField]
    public float Balance = 10f;

    [DataField]
    // How much balance you can recover to while not standing or when initially being effected by balance
    public float InitialBalance = 10f;
    [DataField]
    public float MaxBalance = 50f;

    [DataField]
    public float RecoveryRate = 4f;

    // How much MaxDrunkDuration impacts BalanceDrain
    [DataField]
    public float MaxDrunkMultiplier = 4f;

    // How much drunk time in seconds is needed to reach the MaxDrunkMultiplier
    [DataField]
    public float MaxDrunkDuration = 240f;

    // How much being dragged or moved by conveyorbelts effects BalanceDrain
    [DataField]
    public float ExternalMovementMultiplier = 3f;

    // How much Stamina damage to inflict on reacing 0 Balance
    [DataField]
    public float StaminaDamageOnFall = 35f;

    [DataField]
    public ProtoId<AlertPrototype> BalanceAlert = "Balance";

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField, AutoNetworkedField]
    public TimeSpan NextUpdate = TimeSpan.Zero;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField, AutoNetworkedField]
    public TimeSpan FallAlertUntil = TimeSpan.Zero;

    [ViewVariables]
    public bool Active;

    [ViewVariables]
    public short AlertSeverity = -1;

    public Vector2 LastPosition;
    public MapId LastMapId;
    public bool HasLastPosition;
}

[RegisterComponent]
public sealed partial class ImbalancingClothingComponent : Component
{
    [DataField]
    public float BalanceDrain = 1f;
    [DataField]
    public float BalanceShoveMultiplier = 1f;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class BalanceImmunityComponent : Component;
