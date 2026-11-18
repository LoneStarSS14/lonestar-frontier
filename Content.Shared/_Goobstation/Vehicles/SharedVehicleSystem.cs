using System.Numerics;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Actions;
using Content.Shared.Audio;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Content.Shared._NF.Vehicle.Components; // _CS
using Content.Shared.ActionBlocker; // _CS
using Content.Shared.Actions.Components; // _CS
using Content.Shared.Light.Components; // _CS
using Content.Shared.Light.EntitySystems; // _CS
using Content.Shared.Movement.Pulling.Components; // _CS
using Content.Shared.Movement.Pulling.Events; // _CS
using Content.Shared.Popups; // _CS
using Robust.Shared.Network; // _CS
using Robust.Shared.Physics.Components; // _CS
using Robust.Shared.Physics.Events; // _CS
using Robust.Shared.Physics.Systems; // _CS
using Robust.Shared.Prototypes; // _CS
using Content.Shared.Stunnable; // _CS
using Robust.Shared.Timing; // _CS
using Content.Shared.Throwing; // _CS
using Content.Shared.Weapons.Melee.Events; // _CS
using Content.Shared.Emag.Systems; // _CS

namespace Content.Shared._Goobstation.Vehicles; // _CS: migrate under _Goobstation

public abstract partial class SharedVehicleSystem : EntitySystem
{
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly SharedVirtualItemSystem _virtualItem = default!;
    [Dependency] private readonly INetManager _net = default!; // _CS
    [Dependency] private readonly UnpoweredFlashlightSystem _flashlight = default!; // _CS
    [Dependency] private readonly SharedPopupSystem _popup = default!; // _CS
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!; // _CS
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!; // _CS
    [Dependency] private readonly IGameTiming _timing = default!; // _CS
    [Dependency] private readonly EmagSystem _emag = default!; // _CS
    [Dependency] private readonly MovementSpeedModifierSystem _movespeed = default!; // _CS
    [Dependency] private readonly SharedStunSystem _stun = default!; // _CS
    [Dependency] private readonly ThrowingSystem _throwing = default!; // _CS
    [Dependency] private readonly SharedPhysicsSystem _physics = default!; // _CS

    public static readonly EntProtoId HornActionId = "ActionHorn";
    public static readonly EntProtoId SirenActionId = "ActionSiren";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VehicleComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<VehicleComponent, MapInitEvent>(OnMapInit); // _CS
        SubscribeLocalEvent<VehicleComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<VehicleComponent, StrapAttemptEvent>(OnStrapAttempt);
        SubscribeLocalEvent<VehicleComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<VehicleComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<VehicleComponent, VirtualItemDeletedEvent>(OnDropped);
        SubscribeLocalEvent<VehicleComponent, MeleeHitEvent>(OnMeleeHit); // _CS

        SubscribeLocalEvent<VehicleComponent, EntInsertedIntoContainerMessage>(OnInsert);
        SubscribeLocalEvent<VehicleComponent, EntRemovedFromContainerMessage>(OnEject);

        SubscribeLocalEvent<VehicleComponent, HornActionEvent>(OnHorn);
        SubscribeLocalEvent<VehicleComponent, SirenActionEvent>(OnSiren);
        SubscribeLocalEvent<VehicleComponent, StartCollideEvent>(OnStartCollide); // _CS

        SubscribeLocalEvent<VehicleRiderComponent, PullAttemptEvent>(OnRiderPull); // _CS
        SubscribeLocalEvent<VehicleComponent, GotEmaggedEvent>        (OnVehicleEmagged, before: [typeof(UnpoweredFlashlightSystem)]); // _CS
                SubscribeLocalEvent<VehicleComponent, GotUnEmaggedEvent>(OnVehicleUnemagged, before: [typeof(UnpoweredFlashlightSystem)]); // Frontier
    }

    private void OnInit(EntityUid uid, VehicleComponent component, ComponentInit args)
    {
        _appearance.SetData(uid, VehicleState.Animated, component.EngineRunning && component.Driver != null); // _CS: add Driver != null
        _appearance.SetData(uid, VehicleState.DrawOver, false);
    }

    // _CS: emag removes sprint speed cap, leaving movement bounded only by acceleration vs friction
    private void OnVehicleEmagged(EntityUid uid, VehicleComponent component, ref GotEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
            return;

        // Frontier: emag hides the vehicle radar blip
        if (component.RadarBlip)
        {
            component.RadarBlip = false;
            Dirty(uid, component);
            HandleEmag((uid, component));
            args.Handled = true;
        }
        // End Frontier

        if (TryComp<MovementSpeedModifierComponent>(uid, out var moveComp))
        {
            // Preserve the same effective acceleration (accel * sprintSpeed product is constant).
            const float newSprintSpeed = 9999f;
            var compensatedAccel = moveComp.BaseAcceleration * moveComp.BaseSprintSpeed / newSprintSpeed;
            _movespeed.ChangeBaseSpeed(uid, moveComp.BaseWalkSpeed, newSprintSpeed, compensatedAccel, moveComp);
            args.Handled = true;
        }

        if (args.Handled)
            args.Repeatable = true;
    }
    // _CS End

    // Frontier: restore radar blip when un-emagged
    private void OnVehicleUnemagged(Entity<VehicleComponent> ent, ref GotUnEmaggedEvent args)
    {
        if (args.Handled || !_emag.CompareFlag(args.Type, EmagType.Interaction))
            return;

        if (ent.Comp.RadarBlip)
            return;

        ent.Comp.RadarBlip = true;
        Dirty(ent);
        HandleUnemag(ent);
        args.Handled = true;
    }

    protected abstract void HandleEmag(Entity<VehicleComponent> ent);
    protected abstract void HandleUnemag(Entity<VehicleComponent> ent);
    // End Frontier

    // _CS
    private void OnMapInit(EntityUid uid, VehicleComponent component, MapInitEvent args)
    {
        bool actionsUpdated = false;
        if (component.HornSound != null)
        {
            _actionContainer.EnsureAction(uid, ref component.HornAction, HornActionId);
            actionsUpdated = true;
        }

        if (component.SirenSound != null)
        {
            _actionContainer.EnsureAction(uid, ref component.SirenAction, SirenActionId);
            actionsUpdated = true;
        }

        if (actionsUpdated)
            Dirty(uid, component);
    }
    // _CS End

    private void OnRemove(EntityUid uid, VehicleComponent component, ComponentRemove args)
    {
        if (component.Driver == null)
            return;

        _buckle.TryUnbuckle(component.Driver.Value, component.Driver.Value);
        Dismount(component.Driver.Value, uid);
        _appearance.SetData(uid, VehicleState.DrawOver, false);
    }

    private void OnInsert(EntityUid uid, VehicleComponent component, ref EntInsertedIntoContainerMessage args)
    {
        if (HasComp<InstantActionComponent>(args.Entity))
            return;

        // _CS: check key slot
        if (args.Container.ID != component.KeySlotId)
            return;
        if (!_timing.IsFirstTimePredicted)
            return;
        // _CS End: check key slot

        component.EngineRunning = true;
        _appearance.SetData(uid, VehicleState.Animated, component.Driver != null);

        _ambientSound.SetAmbience(uid, true);

        if (component.Driver == null)
            return;

        Mount(component.Driver.Value, uid);
    }

    private void OnEject(EntityUid uid, VehicleComponent component, ref EntRemovedFromContainerMessage args)
    {
        // _CS: check key slot
        if (args.Container.ID != component.KeySlotId)
            return;
        if (!_timing.IsFirstTimePredicted)
            return;
        // _CS End: check key slot

        component.EngineRunning = false;
        _appearance.SetData(uid, VehicleState.Animated, false);

        _ambientSound.SetAmbience(uid, false);

        if (component.Driver == null)
            return;

        Dismount(component.Driver.Value, uid, removeDriver: false); // _CS: add removeDriver: false - the driver is still around.
    }

    private void OnHorn(EntityUid uid, VehicleComponent component, InstantActionEvent args)
    {
        if (args.Handled == true || component.Driver != args.Performer || component.HornSound == null)
            return;

        _audio.PlayPredicted(component.HornSound, uid, args.Performer); // _CS: PlayPvs<PlayPredicted, add args.Performer
        args.Handled = true;
    }

    private void OnSiren(EntityUid uid, VehicleComponent component, InstantActionEvent args)
    {
        if (_net.IsClient) // _CS: _audio.Stop hates client-side entities, only create this serverside
            return; // _CS

        if (args.Handled == true || component.Driver != args.Performer || component.SirenSound == null)
            return;

        if (component.SirenStream != null) // _CS: SirenEnabled<SirenStream != null
        {
            component.SirenStream = _audio.Stop(component.SirenStream);
        }
        else
        {
            var sirenParams = component.SirenSound.Params.WithLoop(true); // _CS: force loop
            component.SirenStream = _audio.PlayPvs(component.SirenSound, uid, audioParams: sirenParams)?.Entity; // _CS: set params
        }

        // component.SirenEnabled = component.SirenStream != null; // _CS: remove (unneeded state)
        args.Handled = true;
    }


    private void OnStrapAttempt(Entity<VehicleComponent> ent, ref StrapAttemptEvent args)
    {
        var driver = args.Buckle.Owner; // i dont want to re write this shit 100 fucking times

        if (ent.Comp.Driver != null)
        {
            args.Cancelled = true;
            return;
        }

        // _CS: no pulling when riding
        if (TryComp<PullerComponent>(args.Buckle, out var puller) && puller.Pulling != null)
        {
            _popup.PopupPredicted(Loc.GetString("vehicle-cannot-pull", ("object", puller.Pulling), ("vehicle", ent)), ent, args.Buckle);
            args.Cancelled = true;
            return;
        }
        // _CS End

        if (ent.Comp.RequiredHands != 0)
        {
            for (int hands = 0; hands < ent.Comp.RequiredHands; hands++)
            {
                if (!_virtualItem.TrySpawnVirtualItemInHand(ent.Owner, driver, false))
                {
                    args.Cancelled = true;
                    _virtualItem.DeleteInHandsMatching(driver, ent.Owner);
                    return;
                }
            }
        }

        // AddHorns(driver, ent); // _CS: delay until mounted
    }

    protected virtual void OnStrapped(Entity<VehicleComponent> ent, ref StrappedEvent args) // _CS: private<protected virtual
    {
        var driver = args.Buckle.Owner;

        if (!TryComp(driver, out MobMoverComponent? mover) || ent.Comp.Driver != null)
            return;

        ent.Comp.Driver = driver;
        Dirty(ent); // _CS
        _appearance.SetData(ent.Owner, VehicleState.DrawOver, true);
        _appearance.SetData(ent.Owner, VehicleState.Animated, ent.Comp.EngineRunning); // _CS
        var rider = EnsureComp<VehicleRiderComponent>(driver); // _CS
        Dirty(driver, rider); // _CS

        if (!ent.Comp.EngineRunning)
            return;

        Mount(driver, ent.Owner);
    }

    protected virtual void OnUnstrapped(Entity<VehicleComponent> ent, ref UnstrappedEvent args) // _CS: private<protected virtual
    {
        if (ent.Comp.Driver != args.Buckle.Owner)
            return;

        Dismount(args.Buckle.Owner, ent);
        _appearance.SetData(ent.Owner, VehicleState.DrawOver, false);
        _appearance.SetData(ent.Owner, VehicleState.Animated, false); // _CS
        RemComp<VehicleRiderComponent>(args.Buckle.Owner); // _CS
    }

    private void OnDropped(EntityUid uid, VehicleComponent comp, VirtualItemDeletedEvent args)
    {
        if (comp.Driver != args.User)
            return;

        _buckle.TryUnbuckle(args.User, args.User);

        Dismount(args.User, uid);
        _appearance.SetData(uid, VehicleState.DrawOver, false);
        _appearance.SetData(uid, VehicleState.Animated, false); // _CS
        RemComp<VehicleRiderComponent>(args.User); // _CS
    }

    // _CS: do not hit your own vehicle
    private void OnMeleeHit(Entity<VehicleComponent> ent, ref MeleeHitEvent args)
    {
        if (args.User == ent.Comp.Driver) // Don't hit your own vehicle
            args.Handled = true;
    }

    // _CS: high-speed collision ejects the rider.
    private void OnStartCollide(Entity<VehicleComponent> ent, ref StartCollideEvent args)
    {
        if (_net.IsClient)
            return;

        if (ent.Comp.Driver is not { } driver)
            return;

        if (!TryComp<VehicleImpactEjectComponent>(ent, out var ejectComp))
            return;

        if (!args.OurFixture.Hard || !args.OtherFixture.Hard)
            return;

        var preImpactVelocity = args.OurBody.LinearVelocity;
        var speed = preImpactVelocity.Length();
        if (speed < ejectComp.MinimumCollisionSpeed)
            return;

        _buckle.TryUnbuckle(driver, driver);
        _stun.TryKnockdown(driver, TimeSpan.FromSeconds(ejectComp.KnockdownSeconds), true);

        if (!TryComp<PhysicsComponent>(driver, out var driverBody))
            return;

        // Clear any inherited movement before launching the rider in the pre-impact direction.
        _physics.SetLinearVelocity(driver, Vector2.Zero, body: driverBody);
        _throwing.TryThrow(driver, preImpactVelocity, speed, ent, recoil: false, doSpin: false);
    }
    // _CS End: do not hit your own vehicle

    private void AddHorns(EntityUid driver, EntityUid vehicle)
    {
        if (!TryComp<VehicleComponent>(vehicle, out var vehicleComp))
            return;

        // _CS: grant existing actions
        List<EntityUid> grantedActions = new();
        if (vehicleComp.HornAction != null)
            grantedActions.Add(vehicleComp.HornAction.Value);

        if (vehicleComp.SirenAction != null)
            grantedActions.Add(vehicleComp.SirenAction.Value);

        if (TryComp<UnpoweredFlashlightComponent>(vehicle, out var flashlight) && flashlight.ToggleActionEntity != null)
        {
            grantedActions.Add(flashlight.ToggleActionEntity.Value);
            _flashlight.SetLight((vehicle, flashlight), flashlight.LightOn, quiet: true);
        }
        // Only try to grant actions if the vehicle actually has them.
        if (grantedActions.Count > 0)
            _actions.GrantActions(driver, grantedActions, vehicle);
        // _CS End
    }

    private void Mount(EntityUid driver, EntityUid vehicle)
    {
        if (TryComp<AccessComponent>(vehicle, out var accessComp))
        {
            var accessSources = _access.FindPotentialAccessItems(driver);
            var access = _access.FindAccessTags(driver, accessSources);

            foreach (var tag in access)
            {
                accessComp.Tags.Add(tag);
            }
        }

        _mover.SetRelay(driver, vehicle);

        AddHorns(driver, vehicle); // _CS
    }

    private void Dismount(EntityUid driver, EntityUid vehicle, bool removeDriver = true) // _CS: add removeDriver
    {
        if (!TryComp<VehicleComponent>(vehicle, out var vehicleComp) || vehicleComp.Driver != driver)
            return;

        RemComp<RelayInputMoverComponent>(driver);
        _actionBlocker.UpdateCanMove(driver); // _CS: bugfix, relay input mover only updates on shutdown, not remove

        if (removeDriver) // _CS
            vehicleComp.Driver = null;

        _actions.RemoveProvidedActions(driver, vehicle); // _CS: don't remove actions, just provide/revoke them

        if (removeDriver) // _CS
            _virtualItem.DeleteInHandsMatching(driver, vehicle);

        if (TryComp<AccessComponent>(vehicle, out var accessComp))
            accessComp.Tags.Clear();
    }

    // _CS: prevent drivers from pulling things
    private void OnRiderPull(Entity<VehicleRiderComponent> ent, ref PullAttemptEvent args)
    {
        if (args.PullerUid == ent.Owner)
            args.Cancelled = true;
    }
    // _CS End
}

public sealed partial class HornActionEvent : InstantActionEvent;

public sealed partial class SirenActionEvent : InstantActionEvent;
