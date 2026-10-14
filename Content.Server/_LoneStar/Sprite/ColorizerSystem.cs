using Content.Server._LoneStar.Sprite;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared._LoneStar.Sprite;
using Content.Shared.Popups;
using Content.Shared.Sprite;
using Content.Shared.Verbs;
using ColorableClothingComponentT = Content.Shared._LoneStar.Sprite.ColorableClothingComponent;
using Content.Shared.Decals;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;

namespace Content.Server._LoneStar.Sprite;

public sealed class ColorizerSystem : EntitySystem
{
    private readonly List<EntityUid> _actorsToClose = new();

    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ColorableClothingSystem _colorableClothingSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ColorizerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ColorableClothingComponentT, GetVerbsEvent<InteractionVerb>>(OnGetInteractionVerbs);
        SubscribeLocalEvent<ColorizerComponent, ColorizerSelectColorMessage>(OnSelectColor);
        SubscribeLocalEvent<ColorizerComponent, ColorizerSelectVariantMessage>(OnSelectVariant);
        SubscribeLocalEvent<ColorizerComponent, ColorizerRandomizeMessage>(OnRandomize);
        SubscribeLocalEvent<ColorizerComponent, ActivatableUIOpenAttemptEvent>(OnActivatableUIOpenAttempt);
    }
    /// <summary>
    /// Primarily handles closing the UI when the player is too far from the target.
    /// Sadly im not sure if theres any other prebuilt method for this exact case.
    /// </summary>
    /// <param name="frameTime"></param>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ColorizerComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Target is null)
                continue;

            _actorsToClose.Clear();
            foreach (var actor in _uiSystem.GetActors(uid, ColorizerUiKey.Key))
            {
                if (!TryGetAccessibleTarget(uid, component, actor, out _, out _))
                    _actorsToClose.Add(actor);
            }

            foreach (var actor in _actorsToClose)
            {
                _uiSystem.CloseUi(uid, ColorizerUiKey.Key, actor);
            }
        }
    }

    /// <summary>
    /// Handles initiating the BUI of the targeted entity, provided it is valid.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <param name="args"></param>
    private void OnAfterInteract(EntityUid uid, ColorizerComponent component, AfterInteractEvent args)
    {
        if (args.Handled || args.Target is null || !args.CanReach || !IsInPrimaryHand(uid, args.User))
            return;
        // If the target does not have ColorableClothingComponent
        if (!TryComp<ColorableClothingComponentT>(args.Target, out var randomSprite))
            return;
        // If the target is inside a storage/equipped
        if (_inventory.TryGetContainingSlot(args.Target.Value, out _))
        {
            _popup.PopupEntity(Loc.GetString("colorizer-stored"), args.User, args.User, PopupType.Medium);
            return;
        }
        // If the object cant be rerolled
        if (randomSprite.DenyReroll)
        {
            _popup.PopupEntity(Loc.GetString(component.DenyRerollMessage), args.User, args.User, PopupType.Medium);
            return;
        }

        component.Target = args.Target.Value;
        _uiSystem.SetUiState(uid, ColorizerUiKey.Key, GetUiState(args.Target.Value, randomSprite));
        _uiSystem.OpenUi(uid, ColorizerUiKey.Key, args.User);
        args.Handled = true;
    }

    /// <summary>
    /// Builds the UI state from the targeted entities available palettes/mappedColors/variants.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="randomSprite"></param>
    /// <returns></returns>
    private ColorizerBoundUserInterfaceState GetUiState(EntityUid target, ColorableClothingComponentT randomSprite)
    {
        var palettes = new Dictionary<string, Dictionary<string, Color>>();
        var availableVariants = new Dictionary<string, List<string>>();
        var selectedVariants = new Dictionary<string, string>();
        var activeColors = new Dictionary<string, Color>();
        var activeColorNames = new Dictionary<string, string>();
        foreach (var (layer, selected) in randomSprite.Selected)
        {
            selectedVariants[layer] = selected.State;
            var variants = new List<string>();
            foreach (var group in randomSprite.Available)
            {
                if (!group.TryGetValue(layer, out var states))
                    continue;

                foreach (var (state, paletteId) in states)
                {
                    if (!variants.Contains(state))
                        variants.Add(state);
                }

                if (!states.TryGetValue(selected.State, out var selectedPaletteId) ||
                    string.IsNullOrEmpty(selectedPaletteId))
                    continue;

                if (randomSprite.MappedColors.TryGetValue(selectedPaletteId, out var mappedPaletteId))
                {
                    var mappedPalette = _prototype.Index<ColorPalettePrototype>(mappedPaletteId);
                    palettes.TryAdd(selectedPaletteId, mappedPalette.Colors);
                    if (selected.Color is { } mappedColor)
                    {
                        activeColors.TryAdd(selectedPaletteId, mappedColor);
                        AddActiveColorName(activeColorNames, selectedPaletteId, mappedPalette, mappedColor);
                    }
                }
                else if (_prototype.TryIndex<ColorPalettePrototype>(selectedPaletteId, out var selectedPalette))
                {
                    palettes.TryAdd(layer, selectedPalette.Colors);
                    if (selected.Color is { } color)
                    {
                        activeColors.TryAdd(layer, color);
                        AddActiveColorName(activeColorNames, layer, selectedPalette, color);
                    }
                }
            }

            availableVariants[layer] = variants;
        }

        return new ColorizerBoundUserInterfaceState(
            GetNetEntity(target),
            palettes,
            activeColors,
            activeColorNames,
            availableVariants,
            selectedVariants);
    }

    /// <summary>
    /// Gets the active color's name of a given palette
    /// </summary>
    /// <param name="activeColorNames"></param>
    /// <param name="key"></param>
    /// <param name="palette"></param>
    /// <param name="color"></param>
    private static void AddActiveColorName(Dictionary<string, string> activeColorNames, string key, ColorPalettePrototype palette, Color color)
    {
        foreach (var (name, paletteColor) in palette.Colors)
        {
            if (paletteColor != color)
                continue;

            activeColorNames.TryAdd(key, name);
            return;
        }
    }

    /// <summary>
    /// Handles selecting a color on the UI and attempts to apply it.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <param name="args"></param>
    private void OnSelectColor(EntityUid uid, ColorizerComponent component, ColorizerSelectColorMessage args)
    {
        if (!TryGetAccessibleTarget(uid, component, args.Actor, out var target, out var randomSprite))
            return;

        bool changed;
        if (randomSprite.MappedColors.TryGetValue(args.MappedColorKey, out var paletteId))
        {
            var palette = _prototype.Index<ColorPalettePrototype>(paletteId);
            if (!palette.Colors.TryGetValue(args.ColorName, out var color))
                return;

            changed = _colorableClothingSystem.SetMappedColor(target, randomSprite, args.MappedColorKey, color);
        }
        else
        {
            changed = TrySetAvailableColor(target, randomSprite, args.MappedColorKey, args.ColorName);
        }

        if (changed &&
            component.RerollSound != null)
        {
            _audio.PlayPvs(component.RerollSound, target);
        }

        if (changed)
            _uiSystem.SetUiState(uid, ColorizerUiKey.Key, GetUiState(target, randomSprite));
    }

    /// <summary>
    /// Sets a color by looking through Available's keys for layers that match.
    /// </summary>
    /// <param name="target"></param>
    /// <param name="randomSprite"></param>
    /// <param name="layer"></param>
    /// <param name="colorName"></param>
    /// <returns></returns>
    private bool TrySetAvailableColor(EntityUid target, ColorableClothingComponentT randomSprite, string layer, string colorName)
    {
        if (!randomSprite.Selected.TryGetValue(layer, out var selected))
            return false;

        foreach (var group in randomSprite.Available)
        {
            if (!group.TryGetValue(layer, out var states) ||
                !states.TryGetValue(selected.State, out var paletteId) ||
                string.IsNullOrEmpty(paletteId))
                continue;

            var palette = _prototype.Index<ColorPalettePrototype>(paletteId);
            if (!palette.Colors.TryGetValue(colorName, out var color))
                continue;

            randomSprite.Selected[layer] = (selected.State, color);
            Dirty(target, randomSprite);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Applies the selected variant states to the target.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <param name="args"></param>
    private void OnSelectVariant(EntityUid uid, ColorizerComponent component, ColorizerSelectVariantMessage args)
    {
        if (!TryGetAccessibleTarget(uid, component, args.Actor, out var target, out var randomSprite) ||
            !randomSprite.Selected.TryGetValue(args.Layer, out var selected) ||
            selected.State == args.State)
            return;

        foreach (var group in randomSprite.Available)
        {
            if (!group.TryGetValue(args.Layer, out var states) ||
                !states.TryGetValue(args.State, out var paletteId))
                continue;

            states.TryGetValue(selected.State, out var previousPaletteId);
            var color = selected.Color;
            if (string.IsNullOrEmpty(paletteId))
                color = null;
            else if (paletteId != previousPaletteId)
                color = TryGetFirstPaletteColor(randomSprite, paletteId, out var firstColor)
                    ? firstColor
                    : null;

            randomSprite.Selected[args.Layer] = (args.State, color);

            if (!string.IsNullOrEmpty(paletteId) &&
                randomSprite.MappedColors.ContainsKey(paletteId) &&
                color is { } mappedColor)
            {
                if (!_colorableClothingSystem.SetMappedColor(target, randomSprite, paletteId, mappedColor))
                    Dirty(target, randomSprite);
            }
            else
            {
                Dirty(target, randomSprite);
            }

            if (component.RerollSound != null)
                _audio.PlayPvs(component.RerollSound, target);

            _uiSystem.SetUiState(uid, ColorizerUiKey.Key, GetUiState(target, randomSprite));
            return;
        }
    }

    /// <summary>
    /// Gets the first palette color.
    /// </summary>
    /// <param name="randomSprite"></param>
    /// <param name="paletteId"></param>
    /// <param name="color"></param>
    /// <returns></returns>
    private bool TryGetFirstPaletteColor(ColorableClothingComponentT randomSprite, string paletteId, out Color color)
    {
        if (randomSprite.MappedColors.TryGetValue(paletteId, out var mappedPaletteId))
            paletteId = mappedPaletteId;

        var palette = _prototype.Index<ColorPalettePrototype>(paletteId);
        foreach (var (_, paletteColor) in palette.Colors)
        {
            color = paletteColor;
            return true;
        }

        color = default;
        return false;
    }

    /// <summary>
    /// Rerolls the entire selection of variants and palettes.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <param name="args"></param>
    private void OnRandomize(EntityUid uid, ColorizerComponent component, ColorizerRandomizeMessage args)
    {
        if (!TryGetAccessibleTarget(uid, component, args.Actor, out var target, out var randomSprite))
            return;

        if (TryReroll(target, randomSprite, component))
            _uiSystem.SetUiState(uid, ColorizerUiKey.Key, GetUiState(target, randomSprite));
    }

    /// <summary>
    /// Checks if the conditions to update the item are true.
    /// </summary>
    /// <param name="tool"></param>
    /// <param name="component"></param>
    /// <param name="actor"></param>
    /// <param name="target"></param>
    /// <param name="randomSprite"></param>
    /// <returns></returns>
    private bool TryGetAccessibleTarget(EntityUid tool, ColorizerComponent component, EntityUid actor, out EntityUid target, out ColorableClothingComponentT randomSprite)
    {
        target = default;
        randomSprite = default!;

        if (!actor.Valid || !IsInPrimaryHand(tool, actor) ||
            component.Target is not { } targetUid ||
            !TryComp<ColorableClothingComponentT>(targetUid, out var targetSprite) ||
            targetSprite == null ||
            _inventory.TryGetContainingSlot(targetUid, out _) ||
            targetSprite.DenyReroll ||
            !_interactionSystem.InRangeUnobstructed(actor, targetUid))
            return false;

        target = targetUid;
        randomSprite = targetSprite;
        return true;
    }

    /// <summary>
    /// Checks the tool is in the primary active hand.
    /// </summary>
    /// <param name="tool"></param>
    /// <param name="actor"></param>
    /// <returns></returns>
    private bool IsInPrimaryHand(EntityUid tool, EntityUid actor)
    {
        if (!TryComp<HandsComponent>(actor, out var hands) ||
            hands.ActiveHand?.HeldEntity != tool)
            return false;

        return true;
    }

    private void OnActivatableUIOpenAttempt(EntityUid uid, ColorizerComponent component, ActivatableUIOpenAttemptEvent args)
    {
        if (!TryGetAccessibleTarget(uid, component, args.User, out _, out _))
            args.Cancel();
    }

    private bool TryReroll(EntityUid target, ColorableClothingComponentT randomSprite, ColorizerComponent component)
    {
        if (component.RerollSound != null)
            _audio.PlayPvs(component.RerollSound, target);

        return _colorableClothingSystem.Reroll(target, randomSprite);
    }

    /// <summary>
    /// Handles creating the verb menu action.
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="component"></param>
    /// <param name="args"></param>
    private void OnGetInteractionVerbs(EntityUid uid, ColorableClothingComponentT component, GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } usingEntity ||
            !TryComp<ColorizerComponent>(usingEntity, out var reroll))
            return;

        var disabled = false;
        string? message = null;

        if (_inventory.TryGetContainingSlot(uid, out _))
        {
            disabled = true;
            message = Loc.GetString("colorizer-stored");
        }
        else if (component.DenyReroll)
        {
            disabled = true;
            message = Loc.GetString(reroll.DenyRerollMessage);
        }

        args.Verbs.Add(new InteractionVerb
        {
            Act = () =>
            {
                if (!disabled)
                    TryReroll(uid, component, reroll);
            },
            Message = message,
            Disabled = disabled,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/refresh.svg.192dpi.png")),
            Text = Loc.GetString("colorizer-verb-name"),
        });
    }
}
