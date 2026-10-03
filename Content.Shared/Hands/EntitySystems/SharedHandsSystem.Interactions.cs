using System.Linq;
using Content.Shared.Cuffs.Components;
using Content.Shared.Examine;
using Content.Shared.Hands.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Localizations;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared.Hands.EntitySystems;

public abstract partial class SharedHandsSystem : EntitySystem
{
    private void InitializeInteractions()
    {
        SubscribeAllEvent<RequestSetHandEvent>(HandleSetHand);
        SubscribeAllEvent<RequestActivateInHandEvent>(HandleActivateItemInHand);
        SubscribeAllEvent<RequestHandInteractUsingEvent>(HandleInteractUsingInHand);
        SubscribeAllEvent<RequestUseInHandEvent>(HandleUseInHand);
        SubscribeAllEvent<RequestMoveHandItemEvent>(HandleMoveItemFromHand);
        SubscribeAllEvent<RequestHandAltInteractEvent>(HandleHandAltInteract);

        SubscribeLocalEvent<HandsComponent, GetUsedEntityEvent>(OnGetUsedEntity);
        SubscribeLocalEvent<HandsComponent, ExaminedEvent>(HandleExamined);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.UseItemInHand, InputCmdHandler.FromDelegate(HandleUseItem, handle: false, outsidePrediction: false))
            .Bind(ContentKeyFunctions.AltUseItemInHand, InputCmdHandler.FromDelegate(HandleAltUseInHand, handle: false, outsidePrediction: false))
            // .Bind(ContentKeyFunctions.SwapHandsReverse, InputCmdHandler.FromDelegate(SwapHandsPreviousPressed, handle: false, outsidePrediction: false)) // Frontier
            .Bind(ContentKeyFunctions.SwapHands, InputCmdHandler.FromDelegate(SwapHandsPressed, handle: false, outsidePrediction: false))
            .Bind(ContentKeyFunctions.SwapHandsReverse, InputCmdHandler.FromDelegate(SwapHandsReversePressed, handle: false, outsidePrediction: false))
            .Bind(ContentKeyFunctions.Drop, new PointerInputCmdHandler(DropPressed))
            .Register<SharedHandsSystem>();
    }

    #region Event and Key-binding Handlers
    private void HandleAltUseInHand(ICommonSession? session)
    {
        if (session?.AttachedEntity != null)
            TryUseItemInHand(session.AttachedEntity.Value, true);
    }

    private void HandleUseItem(ICommonSession? session)
    {
        if (session?.AttachedEntity != null)
            TryUseItemInHand(session.AttachedEntity.Value);
    }

    private void HandleMoveItemFromHand(RequestMoveHandItemEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity != null)
            TryMoveHeldEntityToActiveHand(args.SenderSession.AttachedEntity.Value, msg.HandName);
    }

    private void HandleUseInHand(RequestUseInHandEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity != null)
            TryUseItemInHand(args.SenderSession.AttachedEntity.Value);
    }

    private void HandleActivateItemInHand(RequestActivateInHandEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity != null)
            TryActivateItemInHand(args.SenderSession.AttachedEntity.Value, null, msg.HandName);
    }

    private void HandleInteractUsingInHand(RequestHandInteractUsingEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity != null)
            TryInteractHandWithActiveHand(args.SenderSession.AttachedEntity.Value, msg.HandName);
    }

    private void HandleHandAltInteract(RequestHandAltInteractEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity != null)
            TryUseItemInHand(args.SenderSession.AttachedEntity.Value, true, handName: msg.HandName);
    }

    private void SwapHandsPressed(ICommonSession? session)
    {
        SwapHands(session, false);
    }

    private void SwapHandsReversePressed(ICommonSession? session)
    {
        SwapHands(session, true);
    }

    private void SwapHands(ICommonSession? session, bool reverse)
    {
        if (!TryComp(session?.AttachedEntity, out HandsComponent? component))
            return;

        if (!_actionBlocker.CanInteract(session.AttachedEntity.Value, null))
            return;

        var swappableHands = component.SortedHands // LoneStar
            .Select(name => component.Hands[name])
            .Where(hand => hand.IsSwappable)
            .ToList();

        if (component.ActiveHand == null || swappableHands.Count == 0)
            return;

        var currentIndex = swappableHands.FindIndex(hand => hand.Name == component.ActiveHand.Name); // LoneStar
        if (currentIndex < 0)
        {
            var fallbackIndex = reverse ? swappableHands.Count - 1 : 0;
            TrySetActiveHand(session.AttachedEntity.Value, swappableHands[fallbackIndex].Name, component);
            return;
        }

        if (swappableHands.Count < 2)
            return;

        var newActiveIndex = (currentIndex + (reverse ? -1 : 1) + swappableHands.Count) % swappableHands.Count;
        var nextHand = swappableHands[newActiveIndex].Name;

        TrySetActiveHand(session.AttachedEntity.Value, nextHand, component);
    }

    private bool DropPressed(ICommonSession? session, EntityCoordinates coords, EntityUid netEntity)
    {
        if (TryComp(session?.AttachedEntity, out HandsComponent? hands) && hands.ActiveHand != null)
            TryDrop(session.AttachedEntity.Value, hands.ActiveHand, coords, handsComp: hands);

        // always send to server.
        return false;
    }
    #endregion

    public bool TryActivateItemInHand(EntityUid uid, HandsComponent? handsComp = null, string? handName = null)
    {
        if (!Resolve(uid, ref handsComp, false))
            return false;

        Hand? hand;
        if (handName == null || !handsComp.Hands.TryGetValue(handName, out hand))
            hand = handsComp.ActiveHand;

        if (hand?.HeldEntity is not { } held)
            return false;

        return _interactionSystem.InteractionActivate(uid, held);
    }

    public bool TryInteractHandWithActiveHand(EntityUid uid, string handName, HandsComponent? handsComp = null)
    {
        if (!Resolve(uid, ref handsComp, false))
            return false;

        if (handsComp.ActiveHandEntity == null)
            return false;

        if (!handsComp.Hands.TryGetValue(handName, out var hand))
            return false;

        if (hand.HeldEntity == null)
            return false;

        _interactionSystem.InteractUsing(uid, handsComp.ActiveHandEntity.Value, hand.HeldEntity.Value, Transform(hand.HeldEntity.Value).Coordinates);
        return true;
    }

    public bool TryUseItemInHand(EntityUid uid, bool altInteract = false, HandsComponent? handsComp = null, string? handName = null)
    {
        if (!Resolve(uid, ref handsComp, false))
            return false;

        Hand? hand;
        if (handName == null || !handsComp.Hands.TryGetValue(handName, out hand))
            hand = handsComp.ActiveHand;

        if (hand?.HeldEntity is not { } held)
            return false;

        if (altInteract)
            return _interactionSystem.AltInteract(uid, held);
        else
            return _interactionSystem.UseInHandInteraction(uid, held);
    }

    /// <summary>
    ///     Moves an entity from one hand to the active hand.
    /// </summary>
    public bool TryMoveHeldEntityToActiveHand(EntityUid uid, string handName, bool checkActionBlocker = true, HandsComponent? handsComp = null)
    {
        if (!Resolve(uid, ref handsComp))
            return false;

        if (handsComp.ActiveHand == null || !handsComp.ActiveHand.IsEmpty)
            return false;

        if (!handsComp.Hands.TryGetValue(handName, out var hand))
            return false;

        if (!CanDropHeld(uid, hand, checkActionBlocker))
            return false;

        var entity = hand.HeldEntity!.Value;

        if (!CanPickupToHand(uid, entity, handsComp.ActiveHand, checkActionBlocker, handsComp))
            return false;

        DoDrop(uid, hand, false, handsComp, log:false);
        DoPickup(uid, handsComp.ActiveHand, entity, handsComp, log: false);
        return true;
    }

    private void OnGetUsedEntity(EntityUid uid, HandsComponent component, ref GetUsedEntityEvent args)
    {
        if (args.Handled)
            return;

        if (component.ActiveHandEntity.HasValue)
        {
            // allow for the item to return a different entity, e.g. virtual items
            RaiseLocalEvent(component.ActiveHandEntity.Value, ref args);
        }

        args.Used ??= component.ActiveHandEntity;
    }

    //TODO: Actually shows all items/clothing/etc.
    private void HandleExamined(EntityUid examinedUid, HandsComponent handsComp, ExaminedEvent args)
    {
        // Floof: total overhaul
        var held = new List<EntityUid>();
        var cuffed = 0;
        foreach (var entity in EnumerateHeld(examinedUid, handsComp))
        {
            if (TryComp(entity, out VirtualItemComponent? virtualItem))
            {
                // Carried or cuffed
                if (TransformSystem.GetParentUid(virtualItem.BlockingEntity) == examinedUid)
                {
                    if (TryComp(examinedUid, out CuffableComponent? cuffable) &&
                        cuffable.Container.ContainedEntities.Contains(virtualItem.BlockingEntity))
                        cuffed++;
                    else
                        held.Add(virtualItem.BlockingEntity);
                }
            }
            else
                held.Add(entity);
        }
        using (args.PushGroup(nameof(HandsComponent), -100))
        {
            var locUser = ("user", Identity.Entity(examinedUid, EntityManager));

            if (cuffed == EnumerateHands(examinedUid, handsComp).Count())
            {
                args.PushMarkup(Loc.GetString("comp-hands-examine-cuffed-all", locUser));
                // If all their hands are cuffed, there's nothing else to do.
                return;
            }
            if (cuffed > 0)
                args.PushMarkup(Loc.GetString("comp-hands-examine-cuffed-some", locUser, ("number", cuffed)));

            var heldItemNames = held
                .Distinct()
                .Select(item => Loc.GetString("comp-hands-examine-wrapper", ("item", item)))
                .ToList();
            var heldKey = heldItemNames.Count != 0 ? "comp-hands-examine" : "comp-hands-examine-empty";
            var heldItems = ("items", ContentLocalizationManager.FormatList(heldItemNames));
            args.PushMarkup(Loc.GetString(heldKey, locUser, heldItems));

            if (_pulling.GetPulling(examinedUid) is { } pulled)
            {
                var pulledItemText = Loc.GetString("comp-hands-examine-wrapper", ("item", pulled));
                args.PushMarkup(Loc.GetString("comp-hands-examine-drag", locUser, ("item", pulledItemText)));
            }
        }
    }
}
