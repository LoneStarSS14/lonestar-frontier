using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._CS.DescriptorQuirks;

/// <summary>
/// This is used for entities to store DescriptorExaminePrototypes and related systems.
/// </summary>
[RegisterComponent]
public sealed partial class DescriptorExamineQuirksComponent : Component
{
    /// <summary>
    /// Showables based on quirktags
    /// </summary>
    [DataField]
    public List<ProtoId<DescriptorExaminePrototype>> DescriptorShowables = new();

    /// <summary>
    /// Showable physical appearances
    /// not descriptor at all
    /// </summary>
    [DataField]
    public HashSet<(string, string)> DescriptorAppearances = new();

    public void AddDescriptorAppearance(string keye)
    {
        var descriptive = $"examine-bodytype-{keye}-descriptive";
        var brief = $"examine-bodytype-{keye}-brief";
        DescriptorAppearances.Add((descriptive, brief));
    }

    public void AddDescriptorExamineTrait(DescriptorExaminePrototype descriptorProto, IPrototypeManager prototypeManager)
    {
        // does this proto already exist in my showables?
        if (DescriptorShowables.Any(showable => showable == descriptorProto.ID))
        {
            return;
        }
        // Check if any of the DescriptorShowables suppress this new proto
        foreach (var showable in DescriptorShowables)
        {
            if (!prototypeManager.TryIndex(showable, out var existingProto))
                continue;
            if (descriptorProto.NeededTag is null)
            {
                continue;
            }
            if (existingProto.SuppressTags.Contains(descriptorProto.NeededTag))
            {
                // don't add this proto, it's suppressed
                return;
            }
        }
        // Remove any existing showables that are suppressed by this new proto
        foreach (var showable in DescriptorShowables.ToList())
        {
            if (!prototypeManager.TryIndex(showable, out var existingProto))
                continue;
            if (existingProto.NeededTag is null
                || string.IsNullOrEmpty(existingProto.NeededTag)
                || descriptorProto.NeededTag is null)
            {
                continue;
            }
            if (descriptorProto.SuppressTags.Contains(existingProto.NeededTag))
            {
                DescriptorShowables.Remove(showable);
            }
        }
        // Finally, add the new proto
        DescriptorShowables.Add(descriptorProto.ID);
    }

    public bool HasTagToShow(ProtoId<DescriptorExaminePrototype>? tagToShow)
    {
        if (string.IsNullOrEmpty(tagToShow))
            return true;
        return DescriptorShowables.Contains(tagToShow.Value);
    }
}

