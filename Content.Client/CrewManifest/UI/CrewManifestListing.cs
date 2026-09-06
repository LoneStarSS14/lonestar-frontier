using Content.Shared.CrewManifest;
using Content.Shared.Roles;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.CrewManifest.UI;

public sealed class CrewManifestListing : BoxContainer
{
    private readonly Dictionary<string, string> _replacements = new() // Lonestar, used for replacements (IF YOU RENAME A JOB UPDATE THIS!!)
    {
        ["ChiefRanger"] = "Sheriff",
        ["Armorer"] = "Bailiff",
        ["SeniorRanger"] = "SeniorOfficer",
        ["Corpsman"] = "Brigmedic",
        ["Ranger"] = "Deputy",
        ["JuniorRanger"] = "Cadet",
        ["Detective"] = "NFDetective",
        ["Janitor"] = "NFJanitor",
        ["Freelancer"] = "NFPirate",
    };

    [Dependency] private readonly IEntitySystemManager _entitySystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    private readonly SpriteSystem _spriteSystem;

    public CrewManifestListing()
    {
        IoCManager.InjectDependencies(this);
        _spriteSystem = _entitySystem.GetEntitySystem<SpriteSystem>();
    }

    // Lonestar: Moved the old logic from AddCrewManifestEntries into a repeat-callable method
    private bool FindDepartmentFromJob(string job, CrewManifestEntry entry, ref Dictionary<DepartmentPrototype, List<CrewManifestEntry>> entryDict)
    {
        foreach (var department in _prototypeManager.EnumeratePrototypes<DepartmentPrototype>())
        {
            if (!department.Roles.Contains(job))
                continue;

            entryDict.GetOrNew(department).Add(entry);
            return true;
        }

        return false;
    }


    public void AddCrewManifestEntries(CrewManifestEntries entries)
    {
        var entryDict = new Dictionary<DepartmentPrototype, List<CrewManifestEntry>>();

        // Lonestar: Rewritten this to call FindDepartmentFromJob & apply substitutions
        foreach (var entry in entries.Entries)
        {
            if (FindDepartmentFromJob(entry.JobPrototype, entry, ref entryDict))
                continue;

            var sanitized = entry.JobTitle.Replace(" ", "").Replace("of", "Of").Replace("Applicant", "Interview");
            if (_replacements.TryGetValue(sanitized, out var mapping))
                sanitized = mapping;

            if (FindDepartmentFromJob(sanitized, entry, ref entryDict))
                continue;

            FindDepartmentFromJob("Contractor", entry, ref entryDict);
        }

        var entryList = new List<(DepartmentPrototype section, List<CrewManifestEntry> entries)>();
        foreach (var (section, listing) in entryDict)
        {
            entryList.Add((section, listing));
        }

        entryList.Sort((a, b) => DepartmentUIComparer.Instance.Compare(a.section, b.section));

        foreach (var item in entryList)
        {
            AddChild(new CrewManifestSection(_prototypeManager, _spriteSystem, item.section, item.entries));
        }
    }
}
