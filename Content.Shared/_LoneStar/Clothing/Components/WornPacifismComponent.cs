using Content.Shared.Inventory;
using Robust.Shared.GameObjects;

namespace Content.Shared._LoneStar.Clothing.Components;

/// <summary>
/// Applies pacifism to the wearer while equipped.
/// </summary>
[RegisterComponent]
public sealed partial class WornPacifismComponent : Component
{
    /// <summary>
    /// Slots in which this component does not apply pacifism.
    /// </summary>
    [DataField]
    public SlotFlags BlockedSlots = SlotFlags.POCKET;

    public bool AddedPacified;
}
