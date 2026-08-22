using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._LoneStar.EntityEffects;

public sealed partial class HasComponent : EntityEffectCondition
{
    [DataField(customTypeSerializer: typeof(ComponentNameSerializer))]
    public string Component = default!;

    [DataField]
    public bool Invert;

    public override bool Condition(EntityEffectBaseArgs args)
    {
        args.EntityManager.ComponentFactory.TryGetRegistration(Component, out var comp);
        return comp == null ? Invert : args.EntityManager.HasComponent(args.TargetEntity, comp) ^ Invert;
    }

    public override string GuidebookExplanation(IPrototypeManager prototype)
    {
        return Loc.GetString("reagent-effect-condition-guidebook-has-component", ("comp", Component), ("invert", Invert));
    }
}
