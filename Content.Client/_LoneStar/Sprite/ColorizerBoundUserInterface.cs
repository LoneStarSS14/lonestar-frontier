using Content.Shared._LoneStar.Sprite;
using Robust.Client.UserInterface;

namespace Content.Client._LoneStar.Sprite;

public sealed class ColorizerBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private ColorizerWindow? _window;

    public ColorizerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<ColorizerWindow>();
        _window.OnColorSelected += (key, colorName) =>
            SendMessage(new ColorizerSelectColorMessage(key, colorName));
        _window.OnVariantSelected += (layer, state) =>
            SendMessage(new ColorizerSelectVariantMessage(layer, state));
        _window.OnRandomize += () => SendMessage(new ColorizerRandomizeMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ColorizerBoundUserInterfaceState paletteState)
            _window?.UpdateState(paletteState);
    }
}
