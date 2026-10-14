using Content.Server._LoneStar.Sprite;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared._LoneStar.Sprite;
using Content.Shared.Popups;
using Content.Shared.Sprite;
using Content.Shared.Verbs;
using Content.Shared.Decals;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;

namespace Content.Server._LoneStar.RandomSprite;

public sealed class RandomSpriteRerollSystem : EntitySystem
{
    private readonly List<EntityUid> _actorsToClose = new();

    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly RandomSpriteSystem _randomSprite = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RandomSpriteRerollComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<RandomSpriteComponent, GetVerbsEvent<InteractionVerb>>(OnGetInteractionVerbs);
        SubscribeLocalEvent<RandomSpriteRerollComponent, RandomSpriteRerollSelectColorMessage>(OnSelectColor);
        SubscribeLocalEvent<RandomSpriteRerollComponent, RandomSpriteRerollSelectVariantMessage>(OnSelectVariant);
        SubscribeLocalEvent<RandomSpriteRerollComponent, RandomSpriteRerollRandomizeMessage>(OnRandomize);
        SubscribeLocalEvent<RandomSpriteRerollComponent, ActivatableUIOpenAttemptEvent>(OnActivatableUIOpenAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<RandomSpriteRerollComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Target is null)
                continue;

            _actorsToClose.Clear();
            foreach (var actor in _uiSystem.GetActors(uid, RandomSpriteRerollUiKey.Key))
            {
                if (!TryGetAccessibleTarget(uid, component, actor, out _, out _))
                    _actorsToClose.Add(actor);
            }

            foreach (var actor in _actorsToClose)
            {
                _uiSystem.CloseUi(uid, RandomSpriteRerollUiKey.Key, actor);
            }
        }
    }

    private void OnAfterInteract(EntityUid uid, RandomSpriteRerollComponent component, AfterInteractEvent args)
    {
        if (args.Handled || args.Target is null || !args.CanReach || !IsInPrimaryHand(uid, args.User))
            return;
        // If the target does not have RandomSpriteComponent
        if (!TryComp<RandomSpriteComponent>(args.Target, out var randomSprite))
            return;
        // If the target is inside a storage/equipped
        if (_inventory.TryGetContainingSlot(args.Target.Value, out _))
        {
            _popup.PopupEntity(Loc.GetString("randomsprite-reroll-stored"), args.User, args.User, PopupType.Medium);
            return;
        }
        // If the object cant be rerolled
        if (randomSprite.DenyReroll)
        {
            _popup.PopupEntity(Loc.GetString(component.DenyRerollMessage), args.User, args.User, PopupType.Medium);
            return;
        }

        component.Target = args.Target.Value;
        _uiSystem.SetUiState(uid, RandomSpriteRerollUiKey.Key, GetUiState(args.Target.Value, randomSprite));
        _uiSystem.OpenUi(uid, RandomSpriteRerollUiKey.Key, args.User);
        args.Handled = true;
    }

    private RandomSpriteRerollBoundUserInterfaceState GetUiState(EntityUid target, RandomSpriteComponent randomSprite)
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

        return new RandomSpriteRerollBoundUserInterfaceState(
            GetNetEntity(target),
            palettes,
            activeColors,
            activeColorNames,
            availableVariants,
            selectedVariants);
    }

    private static void AddActiveColorName(
        Dictionary<string, string> activeColorNames,
        string key,
        ColorPalettePrototype palette,
        Color color)
    {
        foreach (var (name, paletteColor) in palette.Colors)
        {
            if (paletteColor != color)
                continue;

            activeColorNames.TryAdd(key, name);
            return;
        }
    }

    private void OnSelectColor(EntityUid uid, RandomSpriteRerollComponent component, RandomSpriteRerollSelectColorMessage args)
    {
        if (!TryGetAccessibleTarget(uid, component, args.Actor, out var target, out var randomSprite))
            return;

        bool changed;
        if (randomSprite.MappedColors.TryGetValue(args.MappedColorKey, out var paletteId))
        {
            var palette = _prototype.Index<ColorPalettePrototype>(paletteId);
            if (!palette.Colors.TryGetValue(args.ColorName, out var color))
                return;

            changed = _randomSprite.SetMappedColor(target, randomSprite, args.MappedColorKey, color);
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
            _uiSystem.SetUiState(uid, RandomSpriteRerollUiKey.Key, GetUiState(target, randomSprite));
    }

    private bool TrySetAvailableColor(
        EntityUid target,
        RandomSpriteComponent randomSprite,
        string layer,
        string colorName)
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

    private void OnSelectVariant(
        EntityUid uid,
        RandomSpriteRerollComponent component,
        RandomSpriteRerollSelectVariantMessage args)
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
                if (!_randomSprite.SetMappedColor(target, randomSprite, paletteId, mappedColor))
                    Dirty(target, randomSprite);
            }
            else
            {
                Dirty(target, randomSprite);
            }

            if (component.RerollSound != null)
                _audio.PlayPvs(component.RerollSound, target);

            _uiSystem.SetUiState(uid, RandomSpriteRerollUiKey.Key, GetUiState(target, randomSprite));
            return;
        }
    }

    private bool TryGetFirstPaletteColor(RandomSpriteComponent randomSprite, string paletteId, out Color color)
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

    private void OnRandomize(EntityUid uid, RandomSpriteRerollComponent component, RandomSpriteRerollRandomizeMessage args)
    {
        if (!TryGetAccessibleTarget(uid, component, args.Actor, out var target, out var randomSprite))
            return;

        if (TryReroll(target, randomSprite, component))
            _uiSystem.SetUiState(uid, RandomSpriteRerollUiKey.Key, GetUiState(target, randomSprite));
    }

    private bool TryGetAccessibleTarget(
        EntityUid tool,
        RandomSpriteRerollComponent component,
        EntityUid actor,
        out EntityUid target,
        out RandomSpriteComponent randomSprite)
    {
        target = default;
        randomSprite = default!;

        if (!actor.Valid || !IsInPrimaryHand(tool, actor) ||
            component.Target is not { } targetUid ||
            !TryComp<RandomSpriteComponent>(targetUid, out var targetSprite) ||
            targetSprite == null ||
            _inventory.TryGetContainingSlot(targetUid, out _) ||
            targetSprite.DenyReroll ||
            !_interactionSystem.InRangeUnobstructed(actor, targetUid))
            return false;

        target = targetUid;
        randomSprite = targetSprite;
        return true;
    }

    private bool IsInPrimaryHand(EntityUid tool, EntityUid actor)
    {
        if (!TryComp<HandsComponent>(actor, out var hands) ||
            hands.ActiveHand?.HeldEntity != tool)
            return false;

        return true;
    }

    private void OnActivatableUIOpenAttempt(
        EntityUid uid,
        RandomSpriteRerollComponent component,
        ActivatableUIOpenAttemptEvent args)
    {
        if (!TryGetAccessibleTarget(uid, component, args.User, out _, out _))
            args.Cancel();
    }

    private bool TryReroll(EntityUid target, RandomSpriteComponent randomSprite, RandomSpriteRerollComponent component)
    {
        if (component.RerollSound != null)
            _audio.PlayPvs(component.RerollSound, target);

        return _randomSprite.Reroll(target, randomSprite);
    }

    private void OnGetInteractionVerbs(EntityUid uid, RandomSpriteComponent component, GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } usingEntity ||
            !TryComp<RandomSpriteRerollComponent>(usingEntity, out var reroll))
            return;

        var disabled = false;
        string? message = null;

        if (_inventory.TryGetContainingSlot(uid, out _))
        {
            disabled = true;
            message = Loc.GetString("randomsprite-reroll-stored");
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
            Text = Loc.GetString("randomsprite-reroll-verb-name"),
        });
    }
}
