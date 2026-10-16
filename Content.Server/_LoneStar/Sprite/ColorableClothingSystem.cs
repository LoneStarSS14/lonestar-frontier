using System.Linq;
using Content.Shared._LoneStar.Sprite;
using Content.Shared.Decals;
using Content.Shared.Random.Helpers;
using Content.Shared.Sprite;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using ColorableClothingComponentT = Content.Shared._LoneStar.Sprite.ColorableClothingComponent;

namespace Content.Server._LoneStar.Sprite;

public sealed class ColorableClothingSystem : SharedColorableClothingSystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ColorableClothingComponentT, ComponentGetState>(OnGetState);
        SubscribeLocalEvent<ColorableClothingComponentT, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, ColorableClothingComponentT component, MapInitEvent args)
    {
        if (component.Selected.Count > 0)
            return;

        Reroll(uid, component);
    }
    /// <summary>
    /// Randomizes the entire range of options.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <returns></returns>
    public bool Reroll(EntityUid uid, ColorableClothingComponentT component)
    {
        if (component.Available.Count == 0)
            return false;

        component.Selected.Clear();

        // Select mapped colours
        Dictionary<string, Color> mappedColors = new();
        foreach (var (key, value) in component.MappedColors)
        {
            if (_prototype.TryIndex<ColorPalettePrototype>(value, out var palette))
                mappedColors[key] = _random.Pick(palette.Colors.Values);
        }

        var groups = new List<Dictionary<string, Dictionary<string, string?>>>();
        if (component.GetAllGroups)
        {
            groups = component.Available;
        }
        else
        {
            groups.Add(_random.Pick(component.Available));
        }

        component.Selected.EnsureCapacity(groups.Count);

        Color? previousColor = null;

        foreach (var group in groups)
        {
            foreach (var layer in group)
            {
                Color? color = null;

                var selectedState = _random.Pick(layer.Value);
                if (!string.IsNullOrEmpty(selectedState.Value))
                {
                    // Inherit reuses the last direct palette color selected in traversal order.
                    if (selectedState.Value == $"Inherit")
                        color = previousColor;
                    else if (mappedColors.TryGetValue(selectedState.Value, out var mappedColor))
                    {
                        color = mappedColor;
                    }
                    else
                    {
                        color = _random.Pick(_prototype.Index<ColorPalettePrototype>(selectedState.Value).Colors.Values);
                        previousColor = color;
                    }
                }

                component.Selected.Add(layer.Key, (selectedState.Key, color));
            }
        }

        Dirty(uid, component);
        return true;
    }

    /// <summary>
    /// Sets a mappedColor's value to a given color.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <param name="mappedColorKey"></param>
    /// <param name="color"></param>
    /// <returns></returns>
    public bool SetMappedColor(EntityUid uid, ColorableClothingComponentT component, string mappedColorKey, Color color)
    {
        if (!component.MappedColors.ContainsKey(mappedColorKey))
            return false;

        var changed = false;
        foreach (var (layer, selected) in component.Selected.ToArray())
        {
            var usesMappedColor = component.Available.Any(group =>
                group.TryGetValue(layer, out var states) &&
                states.TryGetValue(selected.State, out var value) &&
                value == mappedColorKey);

            if (!usesMappedColor)
                continue;

            component.Selected[layer] = (selected.State, color);
            changed = true;
        }

        if (!changed)
            return false;

        Dirty(uid, component);
        return true;
    }

    private void OnGetState(EntityUid uid, ColorableClothingComponentT component, ref ComponentGetState args)
    {
        args.State = new ColorableClothingColorComponentState()
        {
            Selected = component.Selected,
        };
    }
}
