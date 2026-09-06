using Content.Server.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server._HL.EntityEffects.Effects;

public sealed partial class ChangeBloodReagent : EntityEffect
{
    [DataField(required: true)]
    public string BloodReagent = string.Empty;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("reagent-effect-guidebook-change-blood-reagent", ("reagent", BloodReagent));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args.EntityManager.TryGetComponent<BloodstreamComponent>(args.TargetEntity, out var bloodstream))
        {
            var sys = args.EntityManager.System<BloodstreamSystem>();
            sys.ChangeBloodReagent((args.TargetEntity, bloodstream), BloodReagent);
        }
    }
}
