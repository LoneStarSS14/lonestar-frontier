using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.GameStates;

namespace Content.Shared._CS.AphroLacedVisibility;

/// <summary>
/// Component that keeps track of solution containers to see if they have been injected by aphrodesiacs.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class AphroLacedVisibilityComponent : Component
{
    /// <summary>
    /// The name of the solution of which to check for aphrodesiacs
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("solution")]
    [AutoNetworkedField]
    public string Solution { get; set; } = string.Empty;

    // REVIEW: Maybe instead of the laced bool, I just remove the component using its own system when the aphro runs out.
    // <summary>
    // If this entity has been laced.
    // </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("laced")]
    [AutoNetworkedField]
    public bool Laced { get; set; } = false;

    // <summary>
    // The icon to be used for the status effect.
    // </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("icon")]
    [AutoNetworkedField]
    public ProtoId<SsdIconPrototype> Icon = "AphroLacedSSDIcon";
}
