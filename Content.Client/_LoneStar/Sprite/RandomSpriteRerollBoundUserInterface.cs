using Content.Shared.Sprite;
using Robust.Client.UserInterface;

namespace Content.Client._LoneStar.Sprite;

public sealed class RandomSpriteRerollBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private RandomSpriteRerollWindow? _window;

    public RandomSpriteRerollBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<RandomSpriteRerollWindow>();
        _window.OnColorSelected += (key, colorName) =>
            SendMessage(new RandomSpriteRerollSelectColorMessage(key, colorName));
        _window.OnVariantSelected += (layer, state) =>
            SendMessage(new RandomSpriteRerollSelectVariantMessage(layer, state));
        _window.OnRandomize += () => SendMessage(new RandomSpriteRerollRandomizeMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is RandomSpriteRerollBoundUserInterfaceState paletteState)
            _window?.UpdateState(paletteState);
    }
}
