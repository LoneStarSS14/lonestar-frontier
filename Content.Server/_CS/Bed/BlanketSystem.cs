using Content.Server.Hands.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._CS.Bed;

/// <summary>
///     Allows items tagged as Bedsheet to be applied to another mob by clicking on them,
///     dropping the sheet at their coordinates.
/// </summary>
public sealed class BlanketSystem : EntitySystem
{
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly ProtoId<TagPrototype> BedsheetTag = "Bedsheet";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HandsComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(EntityUid user, HandsComponent hands, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        var used = args.Used;
        var target = args.Target;

        // Must have a target and it must differ from the user (self-equip already works via quick equip).
        if (target == null || target == user)
            return;

        // Item must be tagged as a Bedsheet.
        if (!_tag.HasTag(used, BedsheetTag))
            return;

        // Only apply to mob entities.
        if (!HasComp<MobStateComponent>(target))
            return;

        // Drop the bedsheet from the user's active hand.
        if (!_hands.TryDrop((user, hands), used, checkActionBlocker: false, doDropInteraction: false))
            return;

        // Place the dropped sheet onto the target's tile.
        _transform.SetCoordinates(used, _transform.GetMoverCoordinates(target.Value));

        _popup.PopupEntity(
            Loc.GetString("blanket-covered-self", ("target", target.Value)),
            user, user);
        _popup.PopupEntity(
            Loc.GetString("blanket-covered-target", ("user", user)),
            target.Value, target.Value);

        args.Handled = true;
    }
}
