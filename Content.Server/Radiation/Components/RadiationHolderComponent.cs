using Robust.Shared.GameObjects;

namespace Content.Server.Radiation.Components;

/// <summary>
/// Irradiates the entity holding this item in their hands.
/// </summary>
[RegisterComponent]
public sealed partial class RadiationHolderComponent : Component
{
    /// <summary>
    /// Radiation intensity applied to the holder in rads per second.
    /// </summary>
    [DataField("intensity")]
    public float Intensity = 0.01f;

    /// <summary>
    /// How often radiation is applied.
    /// </summary>
    [DataField("interval")]
    public float Interval = 1f;

    public EntityUid? Holder;
    public float Accumulator;
}
