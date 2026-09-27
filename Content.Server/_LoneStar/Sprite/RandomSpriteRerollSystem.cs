using Content.Server.Sprite;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Sprite;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using System.Reflection;
using Robust.Shared.Utility;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;

namespace Content.Server._LoneStar.RandomSprite;

public sealed class RandomSpriteRerollSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly RandomSpriteSystem _randomSprite = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RandomSpriteRerollComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<RandomSpriteComponent, GetVerbsEvent<InteractionVerb>>(OnGetInteractionVerbs);
    }

    private void OnAfterInteract(EntityUid uid, RandomSpriteRerollComponent component, AfterInteractEvent args)
    {
        if (args.Handled || args.Target is null || !args.CanReach)
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

        if (TryReroll(args.Target.Value, randomSprite, component))
            args.Handled = true;
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
