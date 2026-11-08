using System.Linq;
using Content.Client.Clothing;
using Content.Shared._LoneStar.Sprite;
using Content.Shared.Clothing.Components;
using Content.Shared.Decals;
using Content.Shared.Item;
using Content.Shared.Sprite;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Reflection;

namespace Content.Client.Sprite;

public sealed class ColorableClothingSystem : SharedColorableClothingSystem
{
    [Dependency] private readonly IReflectionManager _reflection = default!;
    [Dependency] private readonly ClientClothingSystem _clothing = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ColorableClothingComponent, ComponentHandleState>(OnHandleState);
        SubscribeLocalEvent<ColorableClothingInitialColorsComponent, ComponentStartup>(OnInitialColorsStartup);
        SubscribeLocalEvent<ColorableClothingInitialColorsComponent, ColorableClothingInitialColorsChangedEvent>(OnInitialColorsChanged);
    }

    private void OnInitialColorsStartup(EntityUid uid, ColorableClothingInitialColorsComponent component, ComponentStartup args)
    {
        if (TryComp<ColorableClothingComponent>(uid, out var colorable))
        {
            ApplyInitialVariants(uid, colorable, component.SelectedVariants);
            ApplyInitialColors(uid, colorable, component.MappedColors);
        }
    }

    private void OnInitialColorsChanged(EntityUid uid, ColorableClothingInitialColorsComponent component, ColorableClothingInitialColorsChangedEvent args)
    {
        if (TryComp<ColorableClothingComponent>(uid, out var colorable))
        {
            ApplyInitialVariants(uid, colorable, component.SelectedVariants);
            ApplyInitialColors(uid, colorable, component.MappedColors);
        }
    }

    private void OnHandleState(EntityUid uid, ColorableClothingComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not ColorableClothingColorComponentState state)
            return;

        if (state.Selected.Equals(component.Selected))
            return;

        component.Selected.Clear();
        component.Selected.EnsureCapacity(state.Selected.Count);

        foreach (var layer in state.Selected)
        {
            component.Selected.Add(layer.Key, layer.Value);
        }

        UpdateSpriteComponentAppearance(uid, component);
        UpdateClothingComponentAppearance(uid, component);
        UpdateItemComponentAppearance(uid, component);
    }

    public void ApplyInitialColors(EntityUid uid, ColorableClothingComponent component, IReadOnlyDictionary<string, Color> colors)
    {
        var groups = component.GetAllGroups
            ? component.Available
            : component.Available.Take(1);

        foreach (var (key, color) in colors)
        {
            foreach (var group in groups)
            {
                foreach (var (layer, states) in group)
                {
                    if (component.Selected.TryGetValue(layer, out var selected) &&
                        states.TryGetValue(selected.State, out var selectedColorKey) &&
                        selectedColorKey == key)
                    {
                        component.Selected[layer] = (selected.State, color);
                        continue;
                    }

                    var state = states.FirstOrDefault(state => state.Value == key);
                    if (state.Key != null)
                        component.Selected[layer] = (state.Key, color);
                }
            }
        }

        UpdateSpriteComponentAppearance(uid, component);
        UpdateClothingComponentAppearance(uid, component);
        UpdateItemComponentAppearance(uid, component);
    }

    public void ApplyInitialVariants(EntityUid uid, ColorableClothingComponent component,
        IReadOnlyDictionary<string, string> variants)
    {
        if (variants.Count == 0)
            return;

        foreach (var (layer, state) in variants)
        {
            if (!TryGetAvailablePalette(component, layer, state, out var paletteKey))
                continue;

            var color = component.Selected.TryGetValue(layer, out var selected) &&
                        TryGetAvailablePalette(component, layer, selected.State, out var previousPaletteKey) &&
                        previousPaletteKey == paletteKey
                ? selected.Color
                : null;
            component.Selected[layer] = (state, color);
        }

        UpdateSpriteComponentAppearance(uid, component);
        UpdateClothingComponentAppearance(uid, component);
        UpdateItemComponentAppearance(uid, component);
    }

    private static bool TryGetAvailablePalette(ColorableClothingComponent component, string layer, string state,
        out string? paletteKey)
    {
        foreach (var group in component.Available)
        {
            if (!group.TryGetValue(layer, out var states) || !states.TryGetValue(state, out paletteKey))
                continue;

            return true;
        }

        paletteKey = null;
        return false;
    }

    private void UpdateClothingComponentAppearance(EntityUid uid, ColorableClothingComponent component, ClothingComponent? clothing = null)
    {
        if (!Resolve(uid, ref clothing, false))
            return;

        foreach (var slotPair in clothing.ClothingVisuals)
        {
            foreach (var keyColorPair in component.Selected)
            {
                _clothing.SetLayerColor(clothing, slotPair.Key, keyColorPair.Key, keyColorPair.Value.Color);
                _clothing.SetLayerState(clothing, slotPair.Key, keyColorPair.Key, keyColorPair.Value.State);
            }
        }
    }

    private void UpdateSpriteComponentAppearance(EntityUid uid, ColorableClothingComponent component, SpriteComponent? sprite = null)
    {
        if (!Resolve(uid, ref sprite, false))
            return;

        foreach (var layer in component.Selected)
        {
            int index;
            if (_reflection.TryParseEnumReference(layer.Key, out var @enum))
            {
                if (!_sprite.LayerMapTryGet((uid, sprite), @enum, out index, logMissing: true))
                    continue;
            }
            else if (!_sprite.LayerMapTryGet((uid, sprite), layer.Key, out index, false))
            {
                if (layer.Key is not { } strKey || !int.TryParse(strKey, out index))
                    continue;
            }
            _sprite.LayerSetRsiState((uid, sprite), index, layer.Value.State);
            _sprite.LayerSetColor((uid, sprite), index, layer.Value.Color ?? Color.White);
        }
    }

    private void UpdateItemComponentAppearance(EntityUid uid, ColorableClothingComponent component, ItemComponent? sprite = null)
    {
        if (!Resolve(uid, ref sprite, false))
            return;

        var itemLayers = sprite.InhandVisuals;
        foreach (var keyColorPair in component.Selected)
        {
            // Update each hand's layers
            foreach (var layerList in itemLayers.Values)
            {
                // Iterate over list of layers, look for our mappings
                foreach (var layer in layerList)
                {
                    if (layer.MapKeys != null && layer.MapKeys.Contains(keyColorPair.Key))
                    {
                        layer.State = keyColorPair.Value.State;
                        layer.Color = keyColorPair.Value.Color;
                    }
                }
            }
        }
    }
}
