using Content.Shared.Roles;
using Robust.Shared.Prototypes;

// Lonestar: why was this in server
namespace Content.Shared.Access.Components;

[RegisterComponent]
public sealed partial class PresetIdCardComponent : Component
{
    [DataField("job")]
    public ProtoId<JobPrototype>? JobName;

    [DataField("name")]
    public string? IdName;
}
