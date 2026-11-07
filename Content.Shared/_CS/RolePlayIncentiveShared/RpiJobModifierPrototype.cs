using Robust.Shared.Prototypes;

namespace Content.Shared._CS.RolePlayIncentiveShared;

/// <summary>
/// This is a prototype for job modifiers that can be applied to roles in the Role Play Incentive system.
/// this way your damn station princess can make a million dollars an hour just by being the queen bee
/// </summary>
[Prototype("rpiJobModifier")]
public sealed partial class RpiJobModifierPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; } = default!;

    /// <summary>
    /// The multiplier applied to the role's RPI.
    /// </summary>
    [DataField("multiplier", required: true), AlwaysPushInheritance]
    public float Multiplier = 1.0f;
}
