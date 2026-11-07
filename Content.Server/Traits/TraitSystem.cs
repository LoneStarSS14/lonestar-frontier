using Content.Shared._CS.DescriptorQuirks;
using Content.Shared._CS.SniffAndSmell;
using Content.Shared.GameTicking;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server.Traits;

public sealed class TraitSystem : EntitySystem
{
    private const string LegacyInHeatTraitId = "descriptorExamineInHeat";
    private const string LegacyInRutTraitId = "descriptorExamineInRut";

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedHandsSystem _sharedHandsSystem = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly ScentSystem _scentSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    // When the player is spawned in, add all trait components selected during character creation
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        // Check if player's job allows to apply traits
        if (args.JobId == null ||
            !_prototypeManager.TryIndex<JobPrototype>(args.JobId ?? string.Empty, out var protoJob) ||
            !protoJob.ApplyTraits)
        {
            return;
        }

        foreach (var traitId in args.Profile.TraitPreferences)
        {
            if (!_prototypeManager.TryIndex<TraitPrototype>(traitId, out var traitPrototype))
            {
                if (traitId != LegacyInHeatTraitId && traitId != LegacyInRutTraitId)
                    Log.Warning($"No trait found with ID {traitId}!");
                continue;
            }

            if (_whitelistSystem.IsWhitelistFail(traitPrototype.Whitelist, args.Mob)
                || _whitelistSystem.IsBlacklistPass(traitPrototype.Blacklist, args.Mob))
                continue;

            // Add all components required by the prototype
            if (traitPrototype.Components is { Count: > 0 })
            {
                EntityManager.AddComponents(
                    args.Mob,
                    traitPrototype.Components,
                    false);
            }

            // Add the descriptor examine stuff, if applicable
            if (traitPrototype.DescriptorExamineProto is not null)
            {
                if (_prototypeManager.TryIndex(traitPrototype.DescriptorExamineProto, out var hormy))
                {
                    EnsureComp<DescriptorExamineQuirksComponent>(args.Mob).AddDescriptorExamineTrait(hormy, _prototypeManager);
                }
            }

            if (traitPrototype.Bodytype is not null)
            {
                EnsureComp<DescriptorExamineQuirksComponent>(args.Mob).AddDescriptorAppearance(traitPrototype.Bodytype);
            }

            if (traitPrototype.Scents is { Count: > 0 })
            {
                var scentComp = EnsureComp<ScentComponent>(args.Mob);
                foreach (var scentProtoId in traitPrototype.Scents)
                {
                    _scentSystem.AddScentPrototype(scentComp, scentProtoId);
                }
            }

            // Add item required by the trait
            if (traitPrototype.TraitGear == null)
                continue;

            if (!TryComp(args.Mob, out HandsComponent? handsComponent))
                continue;

            var coords = Transform(args.Mob).Coordinates;
            var inhandEntity = EntityManager.SpawnEntity(traitPrototype.TraitGear, coords);
            _sharedHandsSystem.TryPickup(args.Mob,
                inhandEntity,
                checkActionBlocker: false,
                handsComp: handsComponent);
        }
    }
}
