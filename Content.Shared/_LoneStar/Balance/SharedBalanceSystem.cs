using System.Numerics;
using Content.Shared.Alert;
using Content.Shared.Clothing.Components;
using Content.Shared.CombatMode;
using Content.Shared.Conveyor;
using Content.Shared.Damage.Systems;
using Content.Shared.Drunk;
using Content.Shared.Gravity;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Standing;
using Content.Shared.StatusEffect;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._LoneStar.Balance;

public abstract class SharedBalanceSystem : EntitySystem
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5f);
    private static readonly TimeSpan FallAlertDuration = TimeSpan.FromSeconds(2);
    private const float RecoveryMultiplierPerSecond = 1.2f;

    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedStaminaSystem _stamina = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    // Ideas for future changes;
    // Add different tiles impacting balance drain
    // Add 'mobile' emotes impacting balance drain, such as jumping or spinning
    // Once we have the low stamina shaking, add that here too
    // Explore low balance making you walk slightly off angle
    // Explore alternative failures such as 'stumbling' (uncontrolled movement in a random direction)

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BalanceComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BalanceComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<BalanceComponent, DisarmedEvent>(OnDisarmed, after: [typeof(SharedStaminaSystem)]);
    }

    private void OnStartup(EntityUid uid, BalanceComponent component, ComponentStartup args)
    {
        component.NextUpdate = _timing.CurTime + UpdateInterval;
    }

    private void OnShutdown(EntityUid uid, BalanceComponent component, ComponentShutdown args)
    {
        _alerts.ClearAlert(uid, component.BalanceAlert);
    }

    private void Activate(EntityUid uid, BalanceComponent component, TimeSpan curTime)
    {
        component.Active = true;
        component.Balance = component.InitialBalance;
        component.NextUpdate = curTime + UpdateInterval;
        component.AlertSeverity = -1;
        component.FallAlertUntil = TimeSpan.Zero;
        UpdateLastPosition(uid, component);
    }

    private void Deactivate(EntityUid uid, BalanceComponent component)
    {
        if (!component.Active)
            return;

        _alerts.ClearAlert(uid, component.BalanceAlert);
        component.Active = false;
        component.Balance = component.MaxBalance;
        component.AlertSeverity = -1;
        component.FallAlertUntil = TimeSpan.Zero;
        component.NextUpdate = _timing.CurTime + UpdateInterval;
        Dirty(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer)
            return;

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<BalanceComponent, InventoryComponent>();
        while (query.MoveNext(out var uid, out var balance, out var inventory))
        {
            if (!balance.Active)
            {
                if (balance.NextUpdate > curTime)
                    continue;

                balance.NextUpdate = curTime + UpdateInterval;
            }

            if (!TryGetImbalancingClothing(uid, inventory, out var balanceDrain, out _)
                || HasComp<BalanceImmunityComponent>(uid))
            {
                Deactivate(uid, balance);
                continue;
            }

            if (!balance.Active)
            {
                Activate(uid, balance, curTime);
                UpdateAlert(uid, balance);
                Dirty(uid, balance);
                continue;
            }

            if (balance.FallAlertUntil != TimeSpan.Zero && balance.FallAlertUntil <= curTime)
            {
                balance.FallAlertUntil = TimeSpan.Zero;
                UpdateAlert(uid, balance);
            }

            if (balance.NextUpdate > curTime)
                continue;

            balance.NextUpdate = curTime + UpdateInterval;

            var isDown = _standing.IsDown(uid);
            var isMoving = HasMoved(uid, balance);
            var isSprinting = false;
            var isWalking = false;
            if (isMoving && TryComp(uid, out InputMoverComponent? mover) && mover.HasDirectionalMovement)
            {
                isSprinting = mover.Sprinting;
                isWalking = !isDown && !mover.Sprinting;
            }
            var isPulled = TryComp<PullableComponent>(uid, out var pullable) && pullable.BeingPulled;
            var isConveyed = TryComp<ConveyedComponent>(uid, out var conveyed) && conveyed.Conveying;
            var isExternallyMoved = isMoving && (isPulled || isConveyed);

            if (!isDown && (isSprinting || isExternallyMoved))
            {
                var multiplier = GetDrunkMultiplier(uid, balance, curTime);
                if (isExternallyMoved)
                    multiplier *= balance.ExternalMovementMultiplier;

                ApplyBalanceLoss(uid, balance, balanceDrain * multiplier * (float)UpdateInterval.TotalSeconds);
            }
            else if (!isWalking)
            {
                var recoveryCap = isDown ? balance.InitialBalance : balance.MaxBalance;
                var intervalMultiplier = MathF.Pow(RecoveryMultiplierPerSecond, (float)UpdateInterval.TotalSeconds);
                var intervalRecovery = balance.RecoveryRate * (intervalMultiplier - 1f) / (RecoveryMultiplierPerSecond - 1f);
                var recoveredBalance = MathF.Min(recoveryCap, balance.Balance * intervalMultiplier + intervalRecovery);
                if (Math.Abs(balance.Balance - recoveredBalance) > float.Epsilon)
                {
                    balance.Balance = recoveredBalance;
                    UpdateAlert(uid, balance);
                    Dirty(uid, balance);
                }
            }
        }
    }

    private bool HasMoved(EntityUid uid, BalanceComponent component)
    {
        if (!TryComp(uid, out TransformComponent? xform))
        {
            component.HasLastPosition = false;
            return false;
        }

        var surface = xform.GridUid ?? xform.MapUid;
        var position = GetSurfacePosition(xform, surface);
        if (!component.HasLastPosition || component.LastSurface != surface)
        {
            component.LastPosition = position;
            component.LastSurface = surface ?? EntityUid.Invalid;
            component.HasLastPosition = true;
            return false;
        }

        var moved = Vector2.DistanceSquared(component.LastPosition, position) > 0.0001f;
        component.LastPosition = position;
        return moved;
    }

    private void UpdateLastPosition(EntityUid uid, BalanceComponent component)
    {
        if (!TryComp(uid, out TransformComponent? xform))
        {
            component.HasLastPosition = false;
            return;
        }

        var surface = xform.GridUid ?? xform.MapUid;
        component.LastPosition = GetSurfacePosition(xform, surface);
        component.LastSurface = surface ?? EntityUid.Invalid;
        component.HasLastPosition = true;
    }

    private Vector2 GetSurfacePosition(TransformComponent xform, EntityUid? surface)
    {
        var worldPosition = _transform.GetWorldPosition(xform);
        return surface is { } surfaceUid
            ? Vector2.Transform(worldPosition, _transform.GetInvWorldMatrix(surfaceUid))
            : worldPosition;
    }

    private void OnDisarmed(EntityUid uid, BalanceComponent balance, ref DisarmedEvent args)
    {
        if (!_net.IsServer
            || !args.Handled
            || args.PopupPrefix != "disarm-action-shove-"
            || !HasComp<ActorComponent>(args.Source)
            || HasComp<BalanceImmunityComponent>(uid)
            || !TryComp<InventoryComponent>(uid, out var inventory)
            || !TryGetImbalancingClothing(uid, inventory, out _, out var shoveMultiplier))
            return;

        var curTime = _timing.CurTime;
        if (!balance.Active)
        {
            Activate(uid, balance, curTime);
            UpdateAlert(uid, balance);
        }

        ApplyBalanceLoss(uid, balance, balance.MaxBalance * 0.2f * shoveMultiplier);
    }

    private bool TryGetImbalancingClothing(
        EntityUid uid,
        InventoryComponent inventory,
        out float balanceDrain,
        out float shoveMultiplier)
    {
        balanceDrain = 0f;
        shoveMultiplier = 0f;
        var found = false;
        var slots = _inventory.GetSlotEnumerator((uid, inventory), SlotFlags.WITHOUT_POCKET);
        while (slots.NextItem(out var item))
        {
            if (!HasComp<ClothingComponent>(item)
                || !TryComp<ImbalancingClothingComponent>(item, out var clothing))
                continue;

            found = true;
            balanceDrain += clothing.BalanceDrain;
            shoveMultiplier += clothing.BalanceShoveMultiplier;
        }

        return found;
    }

    private void ApplyBalanceLoss(EntityUid uid, BalanceComponent component, float amount)
    {
        if (_gravity.IsWeightless(uid))
            return;

        var previousBalance = component.Balance;
        component.Balance = MathF.Max(0f, component.Balance - amount);

        if (previousBalance > 0f && component.Balance <= 0f && !_standing.IsDown(uid) // If we should trigger falling over
            && _standing.Down(uid, dropHeldItems: false))
        {
            _stamina.TakeStaminaDamage(uid, component.StaminaDamageOnFall);
            component.Balance = component.RecoveryRate;
            component.FallAlertUntil = _timing.CurTime + FallAlertDuration;
        }

        if (Math.Abs(previousBalance - component.Balance) <= float.Epsilon)
            return;

        UpdateAlert(uid, component);
        Dirty(uid, component);
    }

    private float GetDrunkMultiplier(EntityUid uid, BalanceComponent component, TimeSpan curTime)
    {
        // Some examples on how ethanol add drunkenness;
        // 5u of ethanol:                                                       100   procs of 2s drunk -> 3m20s peaking at 1m41s
        // 5u of absinthe: 9.804 procs of 0.35u of ethanol -> 3.431u ethanol -> 68.62 procs of 2s drunk -> 2m17s peaking at 1m10s
        // 30u of wine:    60    procs of 0.06u of ethanol -> 3.6u   ethanol -> 72    procs of 2s drunk -> 2m24s peaking at 1m13s

        if (component.MaxDrunkDuration <= 0f
            || component.MaxDrunkMultiplier <= 1f
            || !_statusEffects.TryGetTime(uid, SharedDrunkSystem.DrunkKey, out var drunkEffect)
            || drunkEffect == null)
            return 1f;

        var remaining = Math.Max(0f, (float)(drunkEffect.Value.Item2 - curTime).TotalSeconds);
        var relativeDrunkenness = Math.Clamp(remaining / component.MaxDrunkDuration, 0f, 1f);
        return 1f + (component.MaxDrunkMultiplier - 1f) * relativeDrunkenness;
    }

    private void UpdateAlert(EntityUid uid, BalanceComponent component)
    {
        var severity = component.FallAlertUntil > _timing.CurTime
            ? (short)0
            : (short)(component.MaxBalance > 0f
                ? Math.Clamp((int)MathF.Floor(component.Balance / component.MaxBalance * 5f) + 1, 1, 6)
                : 1);

        if (severity == component.AlertSeverity)
            return;

        _alerts.ShowAlert(uid, component.BalanceAlert, severity);
        component.AlertSeverity = severity;
    }
}
