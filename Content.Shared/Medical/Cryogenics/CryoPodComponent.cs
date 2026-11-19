using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.MedicalScanner;
using Content.Shared.Tools;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
namespace Content.Shared.Medical.Cryogenics;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class CryoPodComponent : Component
{
    /// <summary>
    /// The name of the container the patient is stored in.
    /// </summary>
    public const string BodyContainerName = "scanner-body";

    /// <summary>
    /// The name of the solution container for the injection chamber.
    /// </summary>
    public const string InjectionBufferSolutionName = "injectionBuffer";

    /// <summary>
    /// Specifies the name of the atmospherics port to draw gas from.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("port")]
    public string PortName { get; set; } = "port";

    /// <summary>
    /// Specifies the name of the slot that holds beaker with medicine.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("solutionContainerName")]
    public string SolutionContainerName { get; set; } = "beakerSlot";

    /// <summary>
    /// How often (seconds) are chemicals transferred from the beaker to the body?
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("beakerTransferTime")]
    public TimeSpan BeakerTransferTime = TimeSpan.FromSeconds(2);

    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("nextInjectionTime", customTypeSerializer:typeof(TimeOffsetSerializer))]
    public TimeSpan? NextInjectionTime;

    /// <summary>
    /// How many units of each reagent to transfer per tick from the beaker to the mob?
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("beakerTransferAmount")]
    public float BeakerTransferAmount = .25f; // Frontier: 1<0.25 (applied per reagent)

    // Frontier: more efficient cryogenics (#1443)
    /// <summary>
    /// How potent (multiplier) the reagents are when transferred from the beaker to the mob.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("PotencyAmount")]
    public float PotencyMultiplier = 2f;
    // End Frontier


    /// <summary>
    /// How often the UI is updated.
    /// </summary>
    [DataField]
    public TimeSpan UiUpdateInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The timestamp for the next UI update.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan NextUiUpdateTime = TimeSpan.Zero;

    /// <summary>
    ///     Delay applied when inserting a mob in the pod.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("entryDelay")]
    public float EntryDelay = 2f;

    /// <summary>
    /// Delay applied when trying to pry open a locked pod.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("pryDelay")]
    public float PryDelay = 5f;

    /// <summary>
    /// Container for mobs inserted in the pod.
    /// </summary>
    [ViewVariables]
    public ContainerSlot BodyContainer = default!;

    /// <summary>
    /// If true, the eject verb will not work on the pod and the user must use a crowbar to pry the pod open.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("locked")]
    public bool Locked { get; set; }

    /// <summary>
    /// Causes the pod to be locked without being fixable by messing with wires.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("permaLocked")]
    public bool PermaLocked { get; set; }

    /// <summary>
    /// The tool quality needed to eject a body when the pod is locked.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<ToolQualityPrototype> UnlockToolQuality = "Prying";
}

[Serializable, NetSerializable]
public enum CryoPodVisuals : byte
{
    ContainsEntity,
    IsOn
}

[Serializable, NetSerializable]
public enum CryoPodUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class CryoPodUserMessage : BoundUserInterfaceMessage
{
    public GasAnalyzerComponent.GasMixEntry GasMix;
    public HealthAnalyzerScannedUserMessage Health;
    public FixedPoint2? BeakerCapacity;
    public List<ReagentQuantity>? Beaker;
    public List<ReagentQuantity>? Injecting;

    public CryoPodUserMessage(
        GasAnalyzerComponent.GasMixEntry gasMix,
        HealthAnalyzerScannedUserMessage health,
        FixedPoint2? beakerCapacity,
        List<ReagentQuantity>? beaker,
        List<ReagentQuantity>? injecting)
    {
        GasMix = gasMix;
        Health = health;
        BeakerCapacity = beakerCapacity;
        Beaker = beaker;
        Injecting = injecting;
    }
}

[Serializable, NetSerializable]
public sealed class CryoPodSimpleUiMessage : BoundUserInterfaceMessage
{
    public enum MessageType { EjectPatient, EjectBeaker }

    public readonly MessageType Type;

    public CryoPodSimpleUiMessage(MessageType type)
    {
        Type = type;
    }
}

[Serializable, NetSerializable]
public sealed class CryoPodInjectUiMessage : BoundUserInterfaceMessage
{
    public readonly FixedPoint2 Quantity;

    public CryoPodInjectUiMessage(FixedPoint2 quantity)
    {
        Quantity = quantity;
    }
}
