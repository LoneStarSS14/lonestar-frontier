using Content.Client.Eui;
using Content.Shared.CrewManifest;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client.CrewManifest;

[UsedImplicitly]
public sealed class CrewManifestEui : BaseEui
{
    [Dependency] private readonly SharedCrewManifestSystem _manifest = default!; // Lonestar

    private readonly CrewManifestUi _window;

    public CrewManifestEui()
    {
        IoCManager.InjectDependencies(this); // Lonestar

        _window = new();

        _window.OnClose += () =>
        {
            SendMessage(new CloseEuiMessage());
        };
    }

    public override void Opened()
    {
        base.Opened();

        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();

        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        base.HandleState(state);

        if (state is not CrewManifestEuiState cast)
            return;

        var entries = cast.Entries ?? _manifest.BuildCrewManifest(); // Lonestar
        _window.Populate(entries); // Coyote: Remove name
    }
}
