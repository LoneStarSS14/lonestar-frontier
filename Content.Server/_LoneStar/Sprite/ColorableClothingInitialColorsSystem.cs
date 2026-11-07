using Content.Shared._LoneStar.Sprite;

namespace Content.Server._LoneStar.Sprite;

public sealed class ColorableClothingInitialColorsSystem : EntitySystem
{
    [Dependency] private readonly ColorableClothingSystem _colorableClothing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ColorableClothingInitialColorsComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, ColorableClothingInitialColorsComponent component, MapInitEvent args)
    {
        if (component.MappedColors.Count == 0)
            return;

        if (!TryComp<ColorableClothingComponent>(uid, out var clothing))
        {
            Log.Error($"Entity {ToPrettyString(uid)} has {nameof(ColorableClothingInitialColorsComponent)} but no {nameof(ColorableClothingComponent)}.");
            return;
        }

        if (clothing.Selected.Count == 0 &&
            (!clothing.RandomOnCreation || !_colorableClothing.Reroll(uid, clothing)))
        {
            Log.Warning($"Unable to apply mapped clothing colors to {ToPrettyString(uid)} because it has no selected layers.");
            return;
        }

        foreach (var (key, color) in component.MappedColors)
        {
            if (!clothing.MappedColors.ContainsKey(key))
            {
                Log.Error($"Mapped clothing color '{key}' on {ToPrettyString(uid)} is not defined by {nameof(ColorableClothingComponent)}.");
                continue;
            }

            _colorableClothing.SetMappedColor(uid, clothing, key, color);
        }
    }
}
