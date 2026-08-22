using Content.Shared.Damage.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Timing;

namespace Content.Shared.Damage;

public sealed class PassiveDamageSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PassiveDamageComponent, MapInitEvent>(OnPendingMapInit);
    }

    private void OnPendingMapInit(EntityUid uid, PassiveDamageComponent component, MapInitEvent args)
    {
        component.NextDamage = _timing.CurTime + TimeSpan.FromSeconds(component.Interval); // LoneStar, fixes interval field
    }

    // Every tick, attempt to damage entities
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var curTime = _timing.CurTime;

        // Go through every entity with the component
        var query = EntityQueryEnumerator<PassiveDamageComponent, DamageableComponent>(); // LoneStar
        while (query.MoveNext(out var uid, out var comp, out var damage))
        {
            // Make sure they're up for a damage tick
            if (comp.NextDamage > curTime)
                continue;

            if (comp.DamageCap != 0 && damage.TotalDamage >= comp.DamageCap)
                continue;

            // Set the next time they can take damage
            comp.NextDamage = curTime + TimeSpan.FromSeconds(comp.Interval); // LoneStar, fixes interval field

            // LoneStar, Adds support for entities with no mobstate
            // Damages the entity
            // For entities without MobStateComponent
            if (comp.AllowedStates.Count == 0)
            {
                _damageable.TryChangeDamage(uid, comp.Damage, true, false, damage);
                continue;
            }

            if (!TryComp<MobStateComponent>(uid, out var mobState))
                continue;
            // For entities with MobStateComponent
            foreach (var allowedState in comp.AllowedStates)
            {
                if (allowedState == mobState.CurrentState)
                {
                    _damageable.TryChangeDamage(uid, comp.Damage, true, false, damage);
                    break;
                }
            }
        }
    }
}
