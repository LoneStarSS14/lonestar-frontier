using System.Numerics;
using Content.Shared.Mobs;
using Content.Shared.Movement.Components;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Standing;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client.Silicons.Borgs;

public sealed class BorgVisualPoseSystem : EntitySystem
{
    private const float MovementDeltaEpsilonSquared = 0.0001f;

    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BorgVisualPoseComponent>();
        while (query.MoveNext(out var uid, out var pose))
        {
            var moving = IsMoving(uid, pose);

            if (moving)
            {
                if (!pose.WasMoving
                    && pose.RestState != null
                    && pose.CurrentState == pose.RestState)
                {
                    pose.WakeupUntil = _timing.CurTime + pose.RestWakeupDelay;
                }

                pose.LastMovementTime = _timing.CurTime;
            }
            else
            {
                pose.WakeupUntil = null;
            }

            pose.WasMoving = moving;

            RefreshPose(uid, pose);
        }
    }

    public void Configure(EntityUid uid, BorgTypePrototype prototype)
    {
        var pose = EnsureComp<BorgVisualPoseComponent>(uid);
        pose.IdleState = prototype.SpriteBodyState;
        pose.MovingState = prototype.SpriteBodyMovementState;
        pose.RestState = prototype.SpriteBodyRestState;
        pose.LyingState = prototype.SpriteBodyLyingState;
        pose.DeadState = prototype.SpriteBodyDeadState;
        pose.MindState = prototype.SpriteHasMindState;
        pose.MindMovingState = prototype.SpriteHasMindMovementState;
        pose.NoMindState = prototype.SpriteNoMindState;
        pose.NoMindMovingState = prototype.SpriteNoMindMovementState;
        pose.ToggleLightState = prototype.SpriteToggleLightState;
        pose.ToggleLightMovingState = prototype.SpriteToggleLightMovementState;
        pose.RestDelay = prototype.SpriteBodyRestDelay;
        pose.LastMovementTime = _timing.CurTime;
        pose.CurrentState = null;
        pose.CurrentMindState = null;
        pose.CurrentToggleLightState = null;
        CacheCurrentWorldPosition(uid, pose);

        RefreshPose(uid, pose);
    }

    private bool IsMoving(EntityUid uid, BorgVisualPoseComponent pose)
    {
        var spriteMoving = TryComp<SpriteMovementComponent>(uid, out var movement) && movement.IsMoving;
        if (spriteMoving)
            return true;

        if (!TryComp(uid, out TransformComponent? xform) || xform.MapID == MapId.Nullspace)
        {
            pose.HasLastWorldPosition = false;
            return false;
        }

        var currentPos = _transform.GetWorldPosition(uid);
        if (!pose.HasLastWorldPosition)
        {
            pose.HasLastWorldPosition = true;
            pose.LastWorldMapId = xform.MapID;
            pose.LastWorldPosition = currentPos;
            return false;
        }

        var mapChanged = pose.LastWorldMapId != xform.MapID;
        var distanceSquared = Vector2.DistanceSquared(pose.LastWorldPosition, currentPos);
        pose.LastWorldMapId = xform.MapID;
        pose.LastWorldPosition = currentPos;

        return mapChanged || distanceSquared > MovementDeltaEpsilonSquared;
    }

    private void CacheCurrentWorldPosition(EntityUid uid, BorgVisualPoseComponent pose)
    {
        if (!TryComp(uid, out TransformComponent? xform) || xform.MapID == MapId.Nullspace)
        {
            pose.HasLastWorldPosition = false;
            return;
        }

        pose.HasLastWorldPosition = true;
        pose.LastWorldMapId = xform.MapID;
        pose.LastWorldPosition = _transform.GetWorldPosition(uid);
    }

    private void RefreshPose(
        EntityUid uid,
        BorgVisualPoseComponent pose,
        AppearanceComponent? appearance = null,
        SpriteComponent? sprite = null)
    {
        if (!Resolve(uid, ref appearance, ref sprite, false))
            return;

        var nextState = ResolveState(uid, pose, appearance);
        var movingVisual = pose.MovingState != null && nextState == pose.MovingState;
        if (nextState == pose.CurrentState)
        {
            RefreshIndicators(uid, pose, appearance, sprite, nextState, movingVisual);
            return;
        }

        _sprite.LayerSetRsiState((uid, sprite), BorgVisualLayers.Body, nextState);
        pose.CurrentState = nextState;
        RefreshIndicators(uid, pose, appearance, sprite, nextState, movingVisual);
    }

    private void RefreshIndicators(
        EntityUid uid,
        BorgVisualPoseComponent pose,
        AppearanceComponent appearance,
        SpriteComponent sprite,
        string bodyState,
        bool movingVisual)
    {
        if (bodyState == pose.RestState
            || bodyState == pose.LyingState
            || bodyState == pose.DeadState)
        {
            _sprite.LayerSetVisible((uid, sprite), BorgVisualLayers.Light, false);
            _sprite.LayerSetVisible((uid, sprite), BorgVisualLayers.LightStatus, false);
            return;
        }

        if (!_appearance.TryGetData<bool>(uid, BorgVisuals.HasPlayer, out var hasPlayer, appearance))
            hasPlayer = false;

        if (!TryComp(uid, out BorgChassisComponent? chassis))
            return;

        var mindState = hasPlayer || chassis.BrainEntity != null
            ? (movingVisual ? pose.MindMovingState ?? pose.MindState : pose.MindState)
            : (movingVisual ? pose.NoMindMovingState ?? pose.NoMindState : pose.NoMindState);

        if (mindState != pose.CurrentMindState)
        {
            _sprite.LayerSetRsiState((uid, sprite), BorgVisualLayers.Light, mindState);
            pose.CurrentMindState = mindState;
        }
        _sprite.LayerSetVisible((uid, sprite), BorgVisualLayers.Light, hasPlayer || chassis.BrainEntity != null);

        var toggleLightState = movingVisual ? pose.ToggleLightMovingState ?? pose.ToggleLightState : pose.ToggleLightState;
        if (toggleLightState != pose.CurrentToggleLightState)
        {
            _sprite.LayerSetRsiState((uid, sprite), BorgVisualLayers.LightStatus, toggleLightState);
            pose.CurrentToggleLightState = toggleLightState;
        }
    }

    private string ResolveState(EntityUid uid, BorgVisualPoseComponent pose, AppearanceComponent appearance)
    {
        if (_appearance.TryGetData<MobState>(uid, MobStateVisuals.State, out var mobState, appearance)
            && mobState == MobState.Dead
            && pose.DeadState != null)
        {
            return pose.DeadState;
        }

        if (TryComp<StandingStateComponent>(uid, out var standing)
            && !standing.Standing
            && pose.LyingState != null)
        {
            return pose.LyingState;
        }

        if (pose.WasMoving)
        {
            if (pose.WakeupUntil is { } wakeupUntil && _timing.CurTime < wakeupUntil)
                return pose.IdleState;

            return pose.MovingState ?? pose.IdleState;
        }

        if (pose.RestState != null && _timing.CurTime - pose.LastMovementTime >= pose.RestDelay)
            return pose.RestState;

        return pose.IdleState;
    }
}