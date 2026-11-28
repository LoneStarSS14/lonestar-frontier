using System;
using Content.Server._NF.GC.Components;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Server._LoneStar.Shipyard.Commands;

[AdminCommand(AdminFlags.Fun)]
public sealed class FillStationDocksCommand : IConsoleCommand
{
    [Dependency] private readonly IEntitySystemManager _sysManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "fillstationdocks";
    public string Description => "Sequentially purchases and docks copies of a shuttle until the requested count or first failure.";
    public string Help => $"{Command} <station ID> </Maps/gridfile.yml> <count (1-100)> | {Command} cleanup";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var shipyard = _sysManager.GetEntitySystem<ShipyardSystem>();
        if (args.Length == 1 && args[0].Equals("cleanup", StringComparison.OrdinalIgnoreCase))
        {
            var deleted = shipyard.CleanupDockTestShuttles();
            shell.WriteLine($"Queued {deleted} test shuttle(s) for deletion.");
            return;
        }

        if (args.Length != 3 ||
            !int.TryParse(args[0], out var stationId) ||
            !int.TryParse(args[2], out var count) ||
            count is < 1 or > 100)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        var station = new EntityUid(stationId);
        var path = args[1].Replace('\\', '/');
        if (path.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
            path = path["Resources".Length..];
        if (!path.StartsWith('/'))
            path = $"/{path}";

        var shuttlePath = new ResPath(path);
        var completed = 0;

        void PurchaseNext()
        {
            if (completed >= count)
            {
                shell.WriteLine($"Docked {completed} shuttle(s) at station {stationId}.");
                return;
            }

            if (!shipyard.TryPurchaseShuttleQueued(station, shuttlePath, (success, shuttleUid) =>
                {
                    if (!success || shuttleUid is not { Valid: true } uid)
                    {
                        shell.WriteError($"Stopped after docking {completed} of {count} shuttle(s); the next purchase failed.");
                        return;
                    }

                    _entityManager.EnsureComponent<DeletionCensusExemptComponent>(uid);
                    shipyard.TrackDockTestShuttle(uid);
                    completed++;
                    shell.WriteLine($"Docked shuttle {completed} of {count} at station {stationId}.");
                    PurchaseNext();
                }, out var failureReason))
            {
                var pathHint = failureReason?.StartsWith("Could not load grid file", StringComparison.Ordinal) == true
                    ? " Use a forward-slash resource path such as /Maps/_NF/Shuttles/crescent.yml; press Tab after /Maps/ to autocomplete."
                    : string.Empty;
                shell.WriteError($"Stopped after docking {completed} of {count} shuttle(s): {failureReason}{pathHint}");
            }
        }

        shell.WriteLine($"Starting to dock up to {count} copy/copies of {shuttlePath} at station {stationId}.");
        PurchaseNext();
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                return CompletionResult.FromHint(Loc.GetString("station-id"));
            case 2:
            {
                var resources = IoCManager.Resolve<IResourceManager>();
                var prefix = args[1].Replace('\\', '/');
                if (prefix.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
                    prefix = prefix["Resources".Length..];
                if (string.IsNullOrEmpty(prefix) || prefix.Equals("/Maps", StringComparison.OrdinalIgnoreCase))
                    prefix = "/Maps/";
                else if (!prefix.StartsWith('/'))
                    prefix = $"/{prefix}";

                if (!prefix.StartsWith("/Maps/", StringComparison.OrdinalIgnoreCase))
                    prefix = "/Maps/";

                var options = CompletionHelper.ContentFilePath(prefix, resources);
                return CompletionResult.FromHintOptions(options, "Grid file under /Maps/");
            }
            case 3:
                return CompletionResult.FromHint("Count from 1 to 100");
            default:
                return CompletionResult.Empty;
        }
    }
}
