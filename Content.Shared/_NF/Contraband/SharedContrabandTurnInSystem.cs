using Content.Shared.Contraband;
using Content.Shared.Store; // LoneStar
using Robust.Shared.Containers;
using Robust.Shared.Prototypes; // LoneStar
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Contraband;

[NetSerializable, Serializable]
public enum ContrabandPalletConsoleUiKey : byte
{
    Contraband
}

public abstract class SharedContrabandTurnInSystem : EntitySystem
{
    /// <summary>
    /// Clears contraband turn-in values on an item and everything it contains.
    /// </summary>
    /// <param name="item">The item to clear values on.</param>
    /// <param name="currencies">
    /// If provided, only the turn-in values for these currency types are cleared.
    /// If null, every turn-in value is cleared.
    /// </param>
    public void ClearContrabandValue(EntityUid item, IReadOnlySet<ProtoId<CurrencyPrototype>>? currencies = null)
    {
        // Clear contraband value for printed items
        if (TryComp<ContrabandComponent>(item, out var contraband))
        {
            foreach (var valueKey in contraband.TurnInValues.Keys)
            {
                if (currencies != null && !currencies.Contains(valueKey)) // LoneStar
                    continue;

                contraband.TurnInValues[valueKey] = 0;
            }
        }

        // Recurse into contained entities
        if (TryComp<ContainerManagerComponent>(item, out var containers))
        {
            foreach (var container in containers.Containers.Values)
            {
                foreach (var ent in container.ContainedEntities)
                {
                    ClearContrabandValue(ent, currencies); // LoneStar
                }
            }
        }
    }
}
