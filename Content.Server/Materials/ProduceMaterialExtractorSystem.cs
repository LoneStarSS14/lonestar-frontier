using System.Linq;
using Content.Server.Botany.Components;
using Content.Server.Materials.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Popups;
using Robust.Server.Audio;

namespace Content.Server.Materials;

public sealed class ProduceMaterialExtractorSystem : EntitySystem
{
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<ProduceMaterialExtractorComponent, AfterInteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ProduceMaterialExtractorComponent, FeedProduceEvent>(OnFeedProduce);
    }

    // BEGIN Frontier - Cherry pick wizden#32663
    private void OnInteractUsing(Entity<ProduceMaterialExtractorComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || !this.IsPowered(ent, EntityManager))
            return;

        var success = ExtractFromStorage(ent, args.Used, ref args);
        success |= ExtractFromProduce(ent, args.Used, ref args);

        if (!success)
            return;

        _audio.PlayPvs(ent.Comp.ExtractSound, ent);
        args.Handled = true;
    }

    private void OnFeedProduce(Entity<ProduceMaterialExtractorComponent> ent, ref FeedProduceEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = EatTheProduce(ent, args.Used);
    }

    private bool ExtractFromProduce(Entity<ProduceMaterialExtractorComponent> ent, EntityUid used, ref AfterInteractUsingEvent args)
    {
        if (!TryComp<ProduceComponent>(used, out var produce)
            || !_solutionContainer.TryGetSolution(used, produce.SolutionName, out var solution))
            return false;

        var amount = solution.Value.Comp.Solution.Contents
            .Where(r => ent.Comp.ExtractionReagents.Contains(r.Reagent.Prototype))
            .Sum(r => r.Quantity.Float());
        var changed = (int) amount;

        if (changed == 0)
        {
            _popup.PopupEntity(Loc.GetString("material-extractor-comp-wrongreagent", ("used", used)), args.User, args.User);
            return false;
        }

        _materialStorage.TryChangeMaterialAmount(ent, ent.Comp.ExtractedMaterial, changed);
        QueueDel(used);
        return true;
    }

    private bool ExtractFromStorage(Entity<ProduceMaterialExtractorComponent> ent, EntityUid used, ref AfterInteractUsingEvent args)
    {
        if (!TryComp<StorageComponent>(used, out var storage))
            return false;

        var success = false;
        foreach (var (item, _) in storage.StoredItems)
            success |= ExtractFromProduce(ent, item, ref args);

        return success;
    }

    private bool EatTheProduce(Entity<ProduceMaterialExtractorComponent> ent, EntityUid used)
    {
        if (!this.IsPowered(ent, EntityManager)
            || !TryComp<ProduceComponent>(used, out var produce)
            || !_solutionContainer.TryGetSolution(used, produce.SolutionName, out var solution))
            return false;

        var amount = solution.Value.Comp.Solution.Contents
            .Where(r => ent.Comp.ExtractionReagents.Contains(r.Reagent.Prototype))
            .Sum(r => r.Quantity.Float());
        _materialStorage.TryChangeMaterialAmount(ent, ent.Comp.ExtractedMaterial, (int) amount);
        _audio.PlayPvs(ent.Comp.ExtractSound, ent);
        QueueDel(used);
        return true;
    }
    // END Frontier
}
