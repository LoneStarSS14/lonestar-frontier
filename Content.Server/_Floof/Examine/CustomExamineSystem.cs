using Content.Shared._Floof.Examine;
using Content.Shared._CS.DescriptorQuirks;
using Robust.Shared.Prototypes;


namespace Content.Server._Floof.Examine;


public sealed class CustomExamineSystem : SharedCustomExamineSystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private const string InHeatProtoId = "InHeat";
    private const string InRutProtoId = "InRut";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SetCustomExamineMessage>(OnSetCustomExamineMessage);
    }

    private void OnSetCustomExamineMessage(SetCustomExamineMessage msg, EntitySessionEventArgs args)
    {
        var target = GetEntity(msg.Target);
        if (!CanChangeExamine(args.SenderSession, target))
            return;

        var comp = EnsureComp<CustomExamineComponent>(target);

        TrimData(ref msg.PublicData, ref msg.SubtleData);
        comp.PublicData = msg.PublicData;
        comp.SubtleData = msg.SubtleData;
        comp.InHeatEnabled = msg.InHeatEnabled;
        comp.InRutEnabled = msg.InRutEnabled;

        var descriptorComp = EnsureComp<DescriptorExamineQuirksComponent>(target);
        UpdateShowable(descriptorComp, InHeatProtoId, comp.InHeatEnabled);
        UpdateShowable(descriptorComp, InRutProtoId, comp.InRutEnabled);

        Dirty(target, comp);
    }

    private void UpdateShowable(DescriptorExamineQuirksComponent descriptorComp, string prototypeId, bool enabled)
    {
        if (!_prototypeManager.TryIndex<DescriptorExaminePrototype>(prototypeId, out var proto))
            return;

        if (enabled)
        {
            descriptorComp.AddDescriptorExamineTrait(proto, _prototypeManager);
            return;
        }

        descriptorComp.DescriptorShowables.Remove(proto.ID);
    }
}
