using Content.Shared._CS.RolePlayIncentiveShared;
using Robust.Shared.Prototypes;

namespace Content.Shared._CS;

/// <summary>
/// Marks an entity as a freelancer.
/// </summary>
[RegisterComponent]
public sealed partial class IsFreelancerComponent : Component;

/// <summary>
/// Marks an entity as LSRD personnel.
/// </summary>
[RegisterComponent]
public sealed partial class IsLsrdComponent : Component
{
    /// <summary>
    /// How worth are they
    /// </summary>
    [DataField("worth")]
    public int Worth = 1;
}
