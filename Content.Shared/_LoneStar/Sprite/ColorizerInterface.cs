using Robust.Shared.Serialization;
using Robust.Shared.GameObjects;

namespace Content.Shared._LoneStar.Sprite;

[Serializable, NetSerializable]
public enum ColorizerUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ColorizerBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly NetEntity Target;
    public readonly Dictionary<string, Dictionary<string, Color>> Palettes;
    public readonly Dictionary<string, Color> ActiveColors;
    public readonly Dictionary<string, string> ActiveColorNames;
    public readonly Dictionary<string, List<string>> AvailableVariants;
    public readonly Dictionary<string, string> SelectedVariants;

    public ColorizerBoundUserInterfaceState(
        NetEntity target,
        Dictionary<string, Dictionary<string, Color>> palettes,
        Dictionary<string, Color> activeColors,
        Dictionary<string, string> activeColorNames,
        Dictionary<string, List<string>> availableVariants,
        Dictionary<string, string> selectedVariants)
    {
        Target = target;
        Palettes = palettes;
        ActiveColors = activeColors;
        ActiveColorNames = activeColorNames;
        AvailableVariants = availableVariants;
        SelectedVariants = selectedVariants;
    }
}

[Serializable, NetSerializable]
public sealed class ColorizerSelectColorMessage : BoundUserInterfaceMessage
{
    public readonly string MappedColorKey;
    public readonly string ColorName;

    public ColorizerSelectColorMessage(string mappedColorKey, string colorName)
    {
        MappedColorKey = mappedColorKey;
        ColorName = colorName;
    }
}

[Serializable, NetSerializable]
public sealed class ColorizerSelectVariantMessage : BoundUserInterfaceMessage
{
    public readonly string Layer;
    public readonly string State;

    public ColorizerSelectVariantMessage(string layer, string state)
    {
        Layer = layer;
        State = state;
    }
}

[Serializable, NetSerializable]
public sealed class ColorizerRandomizeMessage : BoundUserInterfaceMessage
{
}
