using System.Linq;
using Content.Shared._LoneStar.Sprite;

namespace Content.Server._LoneStar.Sprite;

public sealed class ColorableClothingInitialColorsSystem : EntitySystem
{
    [Dependency] private readonly ColorableClothingSystem _colorableClothing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ColorableClothingInitialColorsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ColorableClothingInitialColorsComponent, ColorableClothingInitialColorsChangedEvent>(OnColorsChanged);
    }

    private void OnStartup(EntityUid uid, ColorableClothingInitialColorsComponent component, ComponentStartup args)
    {
        ApplyColors(uid, component);
    }

    private void OnColorsChanged(EntityUid uid, ColorableClothingInitialColorsComponent component, ColorableClothingInitialColorsChangedEvent args)
    {
        ApplyColors(uid, component);
    }

    private void ApplyColors(EntityUid uid, ColorableClothingInitialColorsComponent component)
    {
        if (component.MappedColors.Count == 0 && component.SelectedVariants.Count == 0)
            return;

        if (!TryComp<ColorableClothingComponent>(uid, out var clothing))
        {
            Log.Error($"Entity {ToPrettyString(uid)} has {nameof(ColorableClothingInitialColorsComponent)} but no {nameof(ColorableClothingComponent)}.");
            return;
        }

        if (component.MappedColors.Count > 0 && clothing.Selected.Count == 0)
        {
            if (clothing.RandomOnCreation)
            {
                if (!_colorableClothing.Reroll(uid, clothing))
                {
                    Log.Warning($"Unable to apply mapped clothing colors to {ToPrettyString(uid)} because it has no selected layers.");
                    return;
                }
            }
            else
            {
                var groups = clothing.GetAllGroups
                    ? clothing.Available
                    : clothing.Available.Take(1);

                foreach (var group in groups)
                {
                    foreach (var (layer, states) in group)
                    {
                        if (states.Count == 0)
                            continue;

                        var selectedState = states.First();
                        clothing.Selected.TryAdd(layer, (selectedState.Key, null));
                    }
                }

                if (clothing.Selected.Count == 0)
                {
                    Log.Warning($"Unable to apply mapped clothing colors to {ToPrettyString(uid)} because it has no selected layers.");
                    return;
                }
            }
        }

        foreach (var (layer, state) in component.SelectedVariants)
        {
            if (!_colorableClothing.SetVariant(uid, clothing, layer, state))
                Log.Warning($"Unable to apply selected colorable clothing variant '{state}' for layer '{layer}' on {ToPrettyString(uid)}.");
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
