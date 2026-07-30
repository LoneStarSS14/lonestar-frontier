// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Client._Goobstation.Clothing.Components;

[RegisterComponent]
public sealed partial class HideClothingLayerClothingComponent : Component
{
    [DataField(required: true)]
    public HashSet<string> HiddenSlots = new();
}
