using Content.Shared.Movement.Components;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Standing;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client.Silicons.Borgs;

/// <summary>
/// Client side logic for borg type switching. Sets up primarily client-side visual information.
/// </summary>
/// <seealso cref="SharedBorgSwitchableTypeSystem"/>
/// <seealso cref="BorgSwitchableTypeComponent"/>
public sealed class BorgSwitchableTypeSystem : SharedBorgSwitchableTypeSystem
{
    [Dependency] private readonly BorgVisualPoseSystem _borgVisualPose = default!;
    [Dependency] private readonly BorgSystem _borgSystem = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgSwitchableTypeComponent, AfterAutoHandleStateEvent>(AfterStateHandler);
        SubscribeLocalEvent<BorgSwitchableTypeComponent, ComponentStartup>(OnComponentStartup);
    }

    private void OnComponentStartup(Entity<BorgSwitchableTypeComponent> ent, ref ComponentStartup args)
    {
        UpdateEntityAppearance(ent);
    }

    private void AfterStateHandler(Entity<BorgSwitchableTypeComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateEntityAppearance(ent);
    }

    protected override void UpdateEntityAppearance(
        Entity<BorgSwitchableTypeComponent> entity,
        BorgTypePrototype prototype)
    {
        if (TryComp(entity, out SpriteComponent? sprite))
        {
            var rsiPath = new ResPath(prototype.SpriteRsiPath);
            _sprite.LayerSetRsi((entity, sprite), BorgVisualLayers.Body, rsiPath);
            _sprite.LayerSetRsi((entity, sprite), BorgVisualLayers.Light, rsiPath);
            _sprite.LayerSetRsi((entity, sprite), BorgVisualLayers.LightStatus, rsiPath);
            _sprite.LayerSetRsiState((entity, sprite), BorgVisualLayers.Body, prototype.SpriteBodyState);
            _sprite.LayerSetRsiState((entity, sprite), BorgVisualLayers.LightStatus, prototype.SpriteToggleLightState);
        }

        if (TryComp(entity, out BorgChassisComponent? chassis))
        {
            _borgSystem.SetMindStates(
                (entity.Owner, chassis),
                prototype.SpriteHasMindState,
                prototype.SpriteNoMindState);

            if (TryComp(entity, out AppearanceComponent? appearance))
            {
                // Queue update so state changes apply.
                _appearance.QueueUpdate(entity, appearance);
            }
        }

        if (prototype.SpriteBodyMovementState is { } movementState)
        {
            if (prototype.SpriteBodyRestState != null
                || prototype.SpriteBodyLyingState != null
                || prototype.SpriteBodyDeadState != null)
            {
                _borgVisualPose.Configure(entity, prototype);

                // Pose resolver still depends on IsMoving, but body state is driven here instead of SpriteMovement layers.
                var spriteMovement = EnsureComp<SpriteMovementComponent>(entity);
                spriteMovement.NoMovementLayers.Clear();
                spriteMovement.MovementLayers.Clear();
            }
            else
            {
                RemComp<BorgVisualPoseComponent>(entity);

                var spriteMovement = EnsureComp<SpriteMovementComponent>(entity);
                spriteMovement.NoMovementLayers.Clear();
                spriteMovement.NoMovementLayers["movement"] = new PrototypeLayerData
                {
                    State = prototype.SpriteBodyState,
                };
                spriteMovement.MovementLayers.Clear();
                spriteMovement.MovementLayers["movement"] = new PrototypeLayerData
                {
                    State = movementState,
                };
            }
        }
        else
        {
            RemComp<SpriteMovementComponent>(entity);

            if (prototype.SpriteBodyRestState != null
                || prototype.SpriteBodyLyingState != null
                || prototype.SpriteBodyDeadState != null)
            {
                _borgVisualPose.Configure(entity, prototype);
            }
            else
            {
                RemComp<BorgVisualPoseComponent>(entity);
            }
        }

        if ((prototype.SpriteBodyRestState != null
            || prototype.SpriteBodyLyingState != null
            || prototype.SpriteBodyDeadState != null)
            && !HasComp<LayingDownComponent>(entity))
        {
            EnsureComp<LayingDownComponent>(entity);
        }

        base.UpdateEntityAppearance(entity, prototype);
    }
}
