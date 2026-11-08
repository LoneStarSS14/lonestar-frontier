using Content.Shared.Examine;

namespace Content.Shared._CS;

/// <summary>
/// This handles...
/// </summary>
public sealed class CaptureBaitSystem : EntitySystem
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<CaptureBaitComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<CaptureBaitComponent> ent, ref ExaminedEvent args)
    {
        if (!HasComp<IsFreelancerComponent>(args.Examiner))
            return;
        var baiText = Loc.GetString("capture-bait-examine-text");
        args.PushMarkup(baiText);
    }
}
