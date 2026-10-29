using Robust.Shared.Prototypes;

namespace Content.Shared._CS.DescriptorQuirks;

/// <summary>
/// This is used for descriptors visible on players when examining them.
/// </summary>
[Prototype("descriptorExamine")]
public sealed partial class DescriptorExaminePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; } = default!;

    /// <summary>
    /// The tag required to see this examine text, if any
    /// </summary>
    [DataField("neededTag")]
    public string? NeededTag = string.Empty;

    /// <summary>
    /// The text to show if the examiner has the required tag
    /// </summary>
    [DataField("textToShow")]
    public string TextToShow = string.Empty;

    /// <summary>
    /// If populated, will delete other rival quirktags when applied
    /// and prevent them from being applied
    /// </summary>
    [DataField("suppressTags")]
    public List<string> SuppressTags = new();

    /// <summary>
    /// Can the entity see this on themselves?
    /// </summary>
    [DataField("selfExamine")]
    public bool SelfExamine = true;
}
