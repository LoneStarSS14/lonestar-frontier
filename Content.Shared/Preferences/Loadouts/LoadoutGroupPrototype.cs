using Robust.Shared.Prototypes;

namespace Content.Shared.Preferences.Loadouts;

/// <summary>
/// Corresponds to a set of loadouts for a particular slot.
/// </summary>
[Prototype]
public sealed partial class LoadoutGroupPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    /// <summary>
    /// User-friendly name for the group.
    /// </summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// Minimum number of loadouts that need to be specified for this category.
    /// </summary>
    [DataField]
    public int MinLimit = 1;

    /// <summary>
    /// Maximum limit for the category.
    /// </summary>
    [DataField]
    public int MaxLimit = 1;

    /// <summary>
    /// Hides the loadout group from the player.
    /// </summary>
    [DataField]
    public bool Hidden;

    [DataField(required: true)]
    public List<ProtoId<LoadoutPrototype>> Loadouts = new();

    // Frontier: loadout redundancy
    /// <summary>
    /// Loadout subgroups - will be appended to loadout list.
    /// </summary>
    [DataField]
    public List<ProtoId<LoadoutGroupPrototype>> Subgroups = new();

    // LoneStar: subgroup recursivity
    /// <summary>
    /// Gets all unique loadouts in this group and its subgroups.
    /// </summary>
    public List<ProtoId<LoadoutPrototype>> GetAllLoadouts(IPrototypeManager protoManager)
    {
        return CollectLoadouts(protoManager, true);
    }

    /// <summary>
    /// Gets all unique loadouts from subgroups, excluding loadouts defined directly by this group.
    /// </summary>
    public List<ProtoId<LoadoutPrototype>> GetSubgroupLoadouts(IPrototypeManager protoManager)
    {
        var subgroupLoadouts = CollectLoadouts(protoManager, false);
        subgroupLoadouts.RemoveAll(Loadouts.Contains);
        return subgroupLoadouts;
    }

    private List<ProtoId<LoadoutPrototype>> CollectLoadouts(IPrototypeManager protoManager, bool includeRoot)
    {
        var loadouts = new List<ProtoId<LoadoutPrototype>>();
        var visitedGroups = new HashSet<string>();
        var visitedLoadouts = new HashSet<ProtoId<LoadoutPrototype>>();

        void Visit(LoadoutGroupPrototype group, bool includeLoadouts)
        {
            if (!visitedGroups.Add(group.ID))
                return;

            if (includeLoadouts)
            {
                foreach (var loadout in group.Loadouts)
                {
                    if (visitedLoadouts.Add(loadout))
                        loadouts.Add(loadout);
                }
            }

            foreach (var subgroup in group.Subgroups)
            {
                if (protoManager.TryIndex(subgroup, out var subgroupProto))
                    Visit(subgroupProto, true);
            }
        }

        Visit(this, includeRoot);
        return loadouts;
    }
    // End LoneStar
    // End Frontier

    // Frontier: handle unaffordable loadouts
    /// <summary>
    /// Fallback loadouts to be selected in case a character cannot afford them.
    /// Also serves as a default loadout options (up to the maxLimit for a set) for a new character.
    /// </summary>
    [DataField]
    public List<ProtoId<LoadoutPrototype>> Fallbacks = new();
    // End Frontier
}
