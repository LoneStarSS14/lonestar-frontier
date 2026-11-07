namespace Content.Shared._LoneStar.Sprite;

[RegisterComponent]
public sealed partial class ColorableClothingInitialColorsComponent : Component
{
    /// <summary>
    /// Explicit colors keyed by the mapped-color keys used by <see cref="ColorableClothingComponent"/>.
    /// </summary>
    [DataField("mappedColors")]
    public Dictionary<string, Color> MappedColors = new();
}
