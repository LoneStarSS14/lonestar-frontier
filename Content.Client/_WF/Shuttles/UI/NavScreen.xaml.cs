using Content.Shared.Shuttles.BUIStates;

namespace Content.Client.Shuttles.UI;

public sealed partial class NavScreen
{
    public event Action<NetEntity?>? OnAutopilotToggled;

    private bool _autopilotEnabled;

    private void WfInitialize()
    {
        // Autopilot button only enables - clicking it when already enabled does nothing
        AutopilotButton.OnPressed += _ => EnableAutopilot();
    }

    private void EnableAutopilot()
    {
        // Only send enable request if not already enabled
        if (_autopilotEnabled)
        {
            // Keep the button visually pressed since autopilot is still active
            AutopilotButton.Pressed = true;
            return;
        }

        _autopilotEnabled = true;
        AutopilotButton.Pressed = true;
        _entManager.TryGetNetEntity(_shuttleEntity, out var shuttle);
        OnAutopilotToggled?.Invoke(shuttle);
    }

    private void WfUpdateState(NavInterfaceState state)
    {
        // The current navigation state no longer carries autopilot status.
        AutopilotButton.Visible = true;
    }
}
