using Content.Shared.CombatMode.Pacification;
using Content.Shared._LoneStar.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;

namespace Content.Shared._LoneStar.Clothing.EntitySystems;

/// <summary>
/// Applies temporary pacifism while clothing is worn.
/// </summary>
public sealed class WornPacifismSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WornPacifismComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<WornPacifismComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<PacifismEndedEvent>(OnPacifismEnded);
    }

    private void OnEquipped(Entity<WornPacifismComponent> ent, ref GotEquippedEvent args)
    {
        if ((ent.Comp.BlockedSlots & args.SlotFlags) != SlotFlags.NONE)
            return;

        if (HasComp<PacifiedComponent>(args.Equipee))
            return;

        AddComp<PacifiedComponent>(args.Equipee);
        ent.Comp.AddedPacified = true;
    }

    private void OnUnequipped(Entity<WornPacifismComponent> ent, ref GotUnequippedEvent args)
    {
        if ((ent.Comp.BlockedSlots & args.SlotFlags) != SlotFlags.NONE || !ent.Comp.AddedPacified)
            return;

        RemComp<PacifiedComponent>(args.Equipee);
        ent.Comp.AddedPacified = false;
    }

    private void OnPacifismEnded(PacifismEndedEvent args)
    {
        if (HasComp<PacifiedComponent>(args.Entity))
            return;

        foreach (var item in _inventory.GetHandOrInventoryEntities((args.Entity, null, null)))
        {
            if (!TryComp<WornPacifismComponent>(item, out var wornPacifism) ||
                !_inventory.TryGetContainingSlot((item, null, null), out var slot) ||
                (wornPacifism.BlockedSlots & slot.SlotFlags) != SlotFlags.NONE)
                continue;

            AddComp<PacifiedComponent>(args.Entity);
            wornPacifism.AddedPacified = true;
            return;
        }
    }
}
