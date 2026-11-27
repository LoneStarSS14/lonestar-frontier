using System.Linq;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles;

namespace Content.Server._LoneStar.Shuttles.Systems;

public sealed partial class DockingSystem
{
    public bool IsConfigPriority(DockingConfig config, string? priorityTag)
    {
        if (priorityTag is null)
            return false;

        // Priority tags identify the requested target-side port. A tagged shuttle
        // port controls shuttle-side behavior (such as spawn padding), not ranking.
        return config.Docks.Any(docks => IsPriorityDock(docks.DockBUid, priorityTag));
    }

    private bool IsPriorityDock(EntityUid dockUid, string? priorityTag)
    {
        return priorityTag is not null &&
               TryComp<PriorityDockComponent>(dockUid, out var priority) &&
               string.Equals(priority.Tag, priorityTag, StringComparison.Ordinal);
    }
}
