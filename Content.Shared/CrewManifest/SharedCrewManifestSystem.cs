using System.Linq;
using Content.Shared.Access.Systems;
using Content.Shared.Eui;
using Content.Shared.Roles;
using Content.Shared.SSDIndicator;
using Content.Shared.StatusIcon;
using Content.Shared.Access.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CrewManifest;

// Lonestar: Moved BuildCrewManifest() into shared so the client can call it
public abstract partial class SharedCrewManifestSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedIdCardSystem _id = default!; // Coyote

    /// <summary>
    ///     Builds the crew manifest for a station. Stores it in the cache afterwards.
    /// </summary>
    public CrewManifestEntries BuildCrewManifest()
    {
        var targets = EntityQueryEnumerator<SSDIndicatorComponent, MetaDataComponent, ActorComponent>();

        var entries = new CrewManifestEntries();
        var entriesSort = new List<(JobPrototype? job, CrewManifestEntry entry)>();

        while (targets.MoveNext(out var uid, out var indicator, out var meta, out _)) // Coyote
        {
            if (indicator.IsSSD)
                continue;

            var name = meta.EntityName;
            var title = Loc.GetString("suit-sensor-component-unknown-job");
            ProtoId<JobPrototype> job = "thisJobDoesNotExist";
            ProtoId<JobIconPrototype> icon = "JobIconUnknown";

            if (_id.TryFindIdCard(uid, out var card))
            {
                name = card.Comp.FullName ?? name;
                title = card.Comp.LocalizedJobTitle ?? title;
                icon = card.Comp.JobIcon;
                if (TryComp<PresetIdCardComponent>(card, out var preset))
                    job = preset.JobName ?? job;
            }

            var entry = new CrewManifestEntry(name, title, icon, job);
            _prototypeManager.TryIndex<JobPrototype>(job.Id, out var proto);
            entriesSort.Add((proto, entry));
        } // Coyote End

        entriesSort.Sort((a, b) =>
        {
            var cmp = JobUIComparer.Instance.Compare(a.job, b.job);
            return cmp != 0 ? cmp : string.Compare(a.entry.Name, b.entry.Name, StringComparison.CurrentCultureIgnoreCase);
        });

        entries.Entries = entriesSort.Select(x => x.entry).ToArray();
        return entries; // Coyote
    }
}

/// <summary>
///     A message to send to the server when requesting a crew manifest.
///     CrewManifestSystem will open an EUI that will send the crew manifest
///     to the player when it is updated.
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestCrewManifestMessage : EntityEventArgs
{
    public NetEntity Id { get; }

    public RequestCrewManifestMessage(NetEntity id)
    {
        Id = id;
    }
}

[Serializable, NetSerializable]
public sealed class CrewManifestEuiState : EuiStateBase // Coyote: Removed StationName
{
    public CrewManifestEntries? Entries { get; }

    public CrewManifestEuiState(CrewManifestEntries? entries)
    {
        Entries = entries;
    }
}

[Serializable, NetSerializable]
public sealed class CrewManifestEntries
{
    /// <summary>
    ///     Entries in the crew manifest. Goes by department ID.
    /// </summary>
    // public Dictionary<string, List<CrewManifestEntry>> Entries = new();
    public CrewManifestEntry[] Entries = Array.Empty<CrewManifestEntry>();
}

[Serializable, NetSerializable]
public sealed class CrewManifestEntry
{
    public string Name { get; }

    public string JobTitle { get; }

    public string JobIcon { get; }

    public string JobPrototype { get; }

    public CrewManifestEntry(string name, string jobTitle, string jobIcon, string jobPrototype)
    {
        Name = name;
        JobTitle = jobTitle;
        JobIcon = jobIcon;
        JobPrototype = jobPrototype;
    }
}

/// <summary>
///     Tells the server to open a crew manifest UI from
///     this entity's point of view.
/// </summary>
[Serializable, NetSerializable]
public sealed class CrewManifestOpenUiMessage : BoundUserInterfaceMessage
{}
