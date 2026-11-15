using Robust.Shared.Prototypes;

namespace Content.Shared.EntityEffects.Effects.PlantMetabolism;

/// <summary>
///     Handles removal of kudzuification on a plant.
/// </summary>

public sealed partial class PlantWeedKiller : EventEntityEffect<PlantWeedKiller>
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("reagent-effect-guidebook-plant-weed-killer", ("chance", Probability));
}
