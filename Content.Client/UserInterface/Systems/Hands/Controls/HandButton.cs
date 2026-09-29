using Content.Client.UserInterface.Controls;
using Content.Shared.Hands.Components;

namespace Content.Client.UserInterface.Systems.Hands.Controls;

public sealed class HandButton : SlotControl
{
    public HandLocation HandLocation { get; }
    public bool IsModule { get; } // LoneStar

    public HandButton(string handName, HandLocation handLocation, bool isModule = false) // LoneStar
    {
        HandLocation = handLocation;
        IsModule = isModule; // LoneStar
        Name = "hand_" + handName;
        SlotName = handName;
        SetBackground(handLocation);
    }

    private void SetBackground(HandLocation handLoc)
    {
        ButtonTexturePath = handLoc switch
        {
            HandLocation.Left => "Slots/hand_l",
            HandLocation.Middle => "Slots/hand_m",
            HandLocation.Right => "Slots/hand_r",
            _ => ButtonTexturePath
        };
    }
}
