using System.Linq;
using Content.Shared.Decals;
using Content.Shared.Random.Helpers;
using Content.Shared.Sprite;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Sprite;

public sealed class RandomSpriteSystem : SharedRandomSpriteSystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RandomSpriteComponent, ComponentGetState>(OnGetState);
        SubscribeLocalEvent<RandomSpriteComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, RandomSpriteComponent component, MapInitEvent args)
    {
        if (component.Selected.Count > 0)
            return;

        Reroll(uid, component); // LoneStar
    }

    public bool Reroll(EntityUid uid, RandomSpriteComponent component) // LoneStar: Breaks body of code from OnMapInit
    {
        if (component.Available.Count == 0)
            return false; // LoneStar

        component.Selected.Clear(); // LoneStar

        // Frontier: select mapped colours
        Dictionary<string, Color> mappedColors = new();
        foreach (var (key, value) in component.MappedColors)
        {
            if (_prototype.TryIndex<ColorPalettePrototype>(value, out var palette))
                mappedColors[key] = _random.Pick(palette.Colors.Values);
        }
        // End Frontier: select mapped colours

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
                    if (selectedState.Value == $"Inherit")
                        color = previousColor;
                    // Frontier: mapped colours
                    else if (mappedColors.TryGetValue(selectedState.Value, out var mappedColor))
                    {
                        color = mappedColor;
                    }
                    // End Frontier
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
        return true; // LoneStar
    }

    public bool SetMappedColor(EntityUid uid, RandomSpriteComponent component, string mappedColorKey, Color color)
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

    private void OnGetState(EntityUid uid, RandomSpriteComponent component, ref ComponentGetState args)
    {
        args.State = new RandomSpriteColorComponentState()
        {
            Selected = component.Selected,
        };
    }
}
