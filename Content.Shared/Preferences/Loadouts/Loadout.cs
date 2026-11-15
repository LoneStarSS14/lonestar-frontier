using Robust.Shared.Prototypes;
using Robust.Shared.Maths; // LoneStar
using Robust.Shared.Serialization;
using System.Linq; // LoneStar

namespace Content.Shared.Preferences.Loadouts;

/// <summary>
/// Specifies the selected prototype and custom data for a loadout.
/// </summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class Loadout : IEquatable<Loadout>
{
    [DataField]
    public ProtoId<LoadoutPrototype> Prototype;

    // LoneStar start, preserve colorizer selections on each loadout.
    [DataField]
    public Dictionary<string, Color> InitialColors = new();

    [DataField]
    public Dictionary<string, string> InitialVariants = new();
    // LoneStar end

    public bool Equals(Loadout? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        // LoneStar start, Include selected color and layer state in loadout equality.
        return Prototype.Equals(other.Prototype) &&
               InitialColors.Count == other.InitialColors.Count &&
               InitialColors.All(pair =>
                   other.InitialColors.TryGetValue(pair.Key, out var color) && pair.Value.Equals(color)) &&
               InitialVariants.Count == other.InitialVariants.Count &&
               InitialVariants.All(pair =>
                   other.InitialVariants.TryGetValue(pair.Key, out var state) && pair.Value == state);
        // LoneStar end
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is Loadout other && Equals(other);
    }

    public override int GetHashCode()
    {
        // LoneStar start, hash the selected color and layer state consistently with equality.
        var colorsHash = 0;
        foreach (var (key, color) in InitialColors)
        {
            colorsHash ^= HashCode.Combine(key, color);
        }

        var variantsHash = 0;
        foreach (var (key, state) in InitialVariants)
        {
            variantsHash ^= HashCode.Combine(key, state);
        }

        return HashCode.Combine(Prototype, colorsHash, variantsHash);
        // LoneStar end
    }
}
