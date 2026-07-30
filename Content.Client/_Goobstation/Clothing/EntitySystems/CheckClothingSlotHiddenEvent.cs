// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Client._Goobstation.Clothing.EntitySystems;

[ByRefEvent]
public struct CheckClothingSlotHiddenEvent
{
    public string Slot;
    public bool Visible;

    public CheckClothingSlotHiddenEvent(string slot, bool visible = true)
    {
        Slot = slot;
        Visible = visible;
    }
}
