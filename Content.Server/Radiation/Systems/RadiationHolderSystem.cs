using Content.Server.Radiation.Components;
using Content.Shared.Hands;
using Content.Server.Radiation.Systems;

namespace Content.Server.Radiation.Systems;

public sealed class RadiationHolderSystem : EntitySystem
{
    [Dependency]
    private readonly RadiationSystem _radiation = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadiationHolderComponent, GotEquippedHandEvent>(OnEquipped);
        SubscribeLocalEvent<RadiationHolderComponent, GotUnequippedHandEvent>(OnUnequipped);
    }

    private void OnEquipped(
        EntityUid uid,
        RadiationHolderComponent component,
        GotEquippedHandEvent args)
    {
        component.Holder = args.User;
    }

    private void OnUnequipped(
        EntityUid uid,
        RadiationHolderComponent component,
        GotUnequippedHandEvent args)
    {
        component.Holder = null;
        component.Accumulator = 0f;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<RadiationHolderComponent>();

        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Holder is not { } holder)
                continue;

            component.Accumulator += frameTime;

            if (component.Accumulator < component.Interval)
                continue;

            _radiation.IrradiateEntity(
                holder,
                component.Intensity,
                component.Accumulator
            );

            component.Accumulator = 0f;
        }
    }
}
