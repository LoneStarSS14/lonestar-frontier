using System.Linq;
using System.Numerics;
using Content.Server._NF.PublicTransit.Components;
using Content.Server.Shuttles;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._LoneStar.Shuttles.Systems;

public sealed partial class DockingSystem
{
    private sealed class PendingDockingSearch
    {
        // Request inputs.
        public EntityUid ShuttleUid;
        public EntityUid TargetGrid;
        public string? PriorityTag;
        public DockType DockType;

        // Candidate docks and the active priority/fallback pass.
        public List<Entity<DockingComponent>> ShuttleDocks = new();
        public List<Entity<DockingComponent>> AllGridDocks = new();
        public List<Entity<DockingComponent>> ActiveGridDocks = new();
        public int MatchingPriorityDockCount;
        public bool HasPriorityOnlyPass;
        public bool InFallbackPass;
        public int PrimaryPairsChecked;
        public int PairEligibilityRejects;
        public int PairGeometryRejects;
        public int ValidPrimaryPairs;
        public int FinalValidationFailures;
        public string? FailureReason;
        public Dictionary<(Box2 Area, Angle Angle), DockingConfig> Candidates = new();

        // Live geometry used during validation.
        public Entity<MapGridComponent> TargetGridEntity;
        public TransformComponent TargetGridXform = default!;
        public Angle TargetGridAngle;
        public FixturesComponent ShuttleFixtures = default!;
        public Box2 ShuttleAabb;
        public bool IsMap;
        public List<Entity<MapGridComponent>> Grids = new();

        // Resumable search cursors.
        public int ShuttleIndex;
        public int GridIndex;
        public int ValidationIndex = -1;

        // Best result and completion callback.
        public DockingConfig? Best;
        public bool BestPriority;
        public float BestAngle;

        public Action<DockingConfig?>? OnCompleted;
    }

    private readonly List<PendingDockingSearch> _pendingDockingSearches = new();

    public bool QueueDockingConfigSearch(
        EntityUid shuttleUid,
        EntityUid targetGrid,
        Action<DockingConfig?> onCompleted,
        string? priorityTag = null,
        DockType dockType = DockType.Airlock)
    {
        if (!_xformQuery.TryGetComponent(targetGrid, out var targetGridXform) ||
            !_gridQuery.TryGetComponent(targetGrid, out var targetGridComp) ||
            !_gridQuery.TryGetComponent(shuttleUid, out var shuttleGridComp) ||
            !TryComp<FixturesComponent>(shuttleUid, out var shuttleFixtures))
        {
            return false;
        }

        var shuttleDocks = GetDocks(shuttleUid);
        var gridDocks = GetDocks(targetGrid);

        shuttleDocks = shuttleDocks
            .Where(dock =>
                !_tag.HasTag(dock.Owner, PreventFtlTag) &&
                (dock.Comp.DockType & dockType) != DockType.None)
            .ToList();

        gridDocks = gridDocks
            .Where(dock =>
                !_tag.HasTag(dock.Owner, PreventFtlTag) &&
                (dock.Comp.DockType & dockType) != DockType.None &&
                (!HasComp<PriorityDockComponent>(dock.Owner) ||
                 HasComp<TransitShuttleComponent>(shuttleUid)))
            .ToList();

        if (shuttleDocks.Count == 0 || gridDocks.Count == 0)
        {
            onCompleted(null);
            return true;
        }

        var resolvedPriorityTag = priorityTag;
        if (resolvedPriorityTag is null)
        {
            foreach (var dock in shuttleDocks)
            {
                if (TryComp<PriorityDockComponent>(dock.Owner, out var priority) && priority.Tag is not null)
                {
                    resolvedPriorityTag = priority.Tag;
                    break;
                }
            }
        }

        var priorityGridDocks = resolvedPriorityTag is null
            ? []
            : gridDocks.Where(dock => IsPriorityDock(dock.Owner, resolvedPriorityTag)).ToList();

        var activeGridDocks = priorityGridDocks.Count > 0 ? priorityGridDocks : gridDocks;

        _pendingDockingSearches.Add(new PendingDockingSearch
        {
            ShuttleUid = shuttleUid,
            TargetGrid = targetGrid,
            PriorityTag = resolvedPriorityTag,
            DockType = dockType,
            ShuttleDocks = shuttleDocks,
            AllGridDocks = gridDocks,
            ActiveGridDocks = activeGridDocks,
            MatchingPriorityDockCount = priorityGridDocks.Count,
            HasPriorityOnlyPass = priorityGridDocks.Count > 0 && priorityGridDocks.Count < gridDocks.Count,
            TargetGridEntity = (targetGrid, targetGridComp),
            TargetGridXform = targetGridXform,
            TargetGridAngle = _transform.GetWorldRotation(targetGridXform).Reduced(),
            ShuttleFixtures = shuttleFixtures,
            ShuttleAabb = shuttleGridComp.LocalAABB,
            IsMap = HasComp<MapComponent>(targetGrid),
            OnCompleted = onCompleted,
        });

        return true;
    }

    private void ProcessQueuedDockingSearches()
    {
        const int expensiveChecksPerTick = 32;
        const int pairsPerTick = 128;
        var expensiveChecksRemaining = expensiveChecksPerTick;
        var pairsRemaining = pairsPerTick;
        _validSpawnCache.Clear();

        while (expensiveChecksRemaining > 0 && pairsRemaining > 0 && _pendingDockingSearches.Count > 0)
        {
            var request = _pendingDockingSearches[0];

            if (!TryRefreshSearchState(request))
            {
                request.FailureReason = "search state could not be refreshed";
                CompleteSearch(request, null);
                continue;
            }

            if (request.ValidationIndex >= 0)
            {
                if (request.ValidationIndex == request.Best!.Docks.Count)
                {
                    var selectedDocksAvailable = AreSelectedDocksStillAvailable(request);
                    if (!selectedDocksAvailable && TryStartFallbackPass(request))
                        continue;

                    if (!selectedDocksAvailable)
                        request.FailureReason = "selected docks were no longer available";

                    CompleteSearch(request, selectedDocksAvailable ? request.Best : null);
                    continue;
                }

                var (validationShuttleDockUid, validationGridDockUid, _, _) = request.Best.Docks[request.ValidationIndex++];
                expensiveChecksRemaining--;
                if (!TryQueuedDockPair(
                        request,
                        validationShuttleDockUid,
                        validationGridDockUid,
                        request.Best.Coordinates,
                        request.Best.Angle,
                        out _,
                        out _,
                        out _))
                {
                    request.FinalValidationFailures++;
                    if (!TryStartFallbackPass(request))
                    {
                        request.FailureReason = "selected dock pair failed final validation";
                        CompleteSearch(request, null);
                    }
                }

                continue;
            }

            if (request.ShuttleIndex >= request.ShuttleDocks.Count)
            {
                if (request.Best == null && TryStartFallbackPass(request))
                {
                    continue;
                }

                if (request.Best != null)
                {
                    request.ValidationIndex = 0;
                    continue;
                }

                request.FailureReason = "no valid dock pair found";
                CompleteSearch(request, null);
                continue;
            }

            if (request.GridIndex >= request.ActiveGridDocks.Count)
            {
                request.ShuttleIndex++;
                request.GridIndex = 0;
                continue;
            }

            var (dockUid, shuttleDock) = request.ShuttleDocks[request.ShuttleIndex];
            var (gridDockUid, gridDock) = request.ActiveGridDocks[request.GridIndex];

            request.GridIndex++;
            pairsRemaining--;
            request.PrimaryPairsChecked++;
            if (!TryResolveQueuedDockPair(request, dockUid, gridDockUid,
                    out _, out _, out _, out _))
            {
                request.PairEligibilityRejects++;
                continue;
            }
            expensiveChecksRemaining--;

            if (!TryQueuedDockPair(
                    request,
                    dockUid,
                    gridDockUid,
                    null,
                    null,
                    out var matty,
                    out var dockedAabb,
                    out var targetAngle))
            {
                request.PairGeometryRejects++;
                continue;
            }

            request.ValidPrimaryPairs++;
            var area = dockedAabb.Rounded(DockRoundingDigits);
            var key = (area, targetAngle);
            if (!request.Candidates.TryGetValue(key, out var candidate))
            {
                candidate = new DockingConfig
                {
                    Coordinates = new EntityCoordinates(request.TargetGrid, Vector2.Transform(Vector2.Zero, matty)),
                    Area = area,
                    Angle = targetAngle,
                    TargetGrid = request.TargetGrid,
                };
                request.Candidates.Add(key, candidate);
            }

            candidate.Docks.Add((dockUid, gridDockUid, shuttleDock, gridDock));
            ConsiderCandidate(request, candidate);
        }
    }

    private bool TryRefreshSearchState(PendingDockingSearch request)
    {
        if (Terminating(request.ShuttleUid) ||
            Terminating(request.TargetGrid) ||
            !_gridQuery.TryGetComponent(request.ShuttleUid, out var shuttleGrid) ||
            !_gridQuery.TryGetComponent(request.TargetGrid, out var targetGrid) ||
            !_xformQuery.TryGetComponent(request.TargetGrid, out var targetXform) ||
            targetXform.MapUid == null ||
            !TryComp<FixturesComponent>(request.ShuttleUid, out var fixtures))
        {
            return false;
        }

        request.ShuttleAabb = shuttleGrid.LocalAABB;
        request.TargetGridEntity = (request.TargetGrid, targetGrid);
        request.ShuttleFixtures = fixtures;
        request.TargetGridXform = targetXform;
        request.TargetGridAngle = _transform.GetWorldRotation(targetXform).Reduced();
        return true;
    }

    private bool TryStartFallbackPass(PendingDockingSearch request)
    {
        if (request.InFallbackPass || !request.HasPriorityOnlyPass)
            return false;

        request.InFallbackPass = true;
        request.ActiveGridDocks = request.AllGridDocks;
        request.ShuttleIndex = 0;
        request.GridIndex = 0;
        request.Candidates.Clear();
        request.ValidationIndex = -1;
        request.Best = null;
        request.BestPriority = false;
        request.BestAngle = 0f;
        return true;
    }

    private bool AreSelectedDocksStillAvailable(PendingDockingSearch request)
    {
        foreach (var (dockAUid, dockBUid, dockA, dockB) in request.Best!.Docks)
        {
            if (!TryComp<DockingComponent>(dockAUid, out var currentA) ||
                currentA != dockA || currentA.Docked ||
                !TryComp<DockingComponent>(dockBUid, out var currentB) ||
                currentB != dockB || currentB.Docked)
            {
                return false;
            }
        }

        return true;
    }

    private void CompleteSearch(PendingDockingSearch request, DockingConfig? result)
    {
        _pendingDockingSearches.Remove(request);
        if (result == null && HasComp<TransitShuttleComponent>(request.ShuttleUid))
        {
            Log.Debug($"Transit docking search failed for {ToPrettyString(request.ShuttleUid)} -> {ToPrettyString(request.TargetGrid)} (priority tag: {request.PriorityTag ?? "none"}; shuttle docks: {request.ShuttleDocks.Count}; target docks: {request.AllGridDocks.Count}; matching priority docks: {request.MatchingPriorityDockCount}; fallback pass: {request.InFallbackPass}; primary pairs checked: {request.PrimaryPairsChecked}; eligibility rejects: {request.PairEligibilityRejects}; geometry rejects: {request.PairGeometryRejects}; valid primary pairs: {request.ValidPrimaryPairs}; final validation failures: {request.FinalValidationFailures}; reason: {request.FailureReason ?? "unspecified"}).");
        }

        request.OnCompleted?.Invoke(result);
    }

    private void ConsiderCandidate(PendingDockingSearch request, DockingConfig candidate)
    {
        var candidatePriority = IsConfigPriority(candidate, request.PriorityTag);
        var candidateAngle = (float) Math.Abs(
            Angle.ShortestDistance(candidate.Angle.Reduced(), request.TargetGridAngle).Theta);
        var hasBetterPriority = candidatePriority && !request.BestPriority;
        var samePriority = candidatePriority == request.BestPriority;
        var hasBetterDockCount = request.Best != null && candidate.Docks.Count > request.Best.Docks.Count;
        var sameDockCount = request.Best != null && candidate.Docks.Count == request.Best.Docks.Count;
        var hasBetterAngle = sameDockCount && candidateAngle < request.BestAngle;

        if (request.Best == null ||
            hasBetterPriority ||
            samePriority && (hasBetterDockCount || hasBetterAngle))
        {
            request.Best = candidate;
            request.BestPriority = candidatePriority;
            request.BestAngle = candidateAngle;
        }
    }

    private bool TryQueuedDockPair(
        PendingDockingSearch request,
        EntityUid shuttleDockUid,
        EntityUid gridDockUid,
        EntityCoordinates? coordinates,
        Angle? angle,
        out Matrix3x2 transform,
        out Box2 bounds,
        out Angle rotation)
    {
        transform = default;
        bounds = default;
        rotation = default;
        if (!TryResolveQueuedDockPair(request, shuttleDockUid, gridDockUid,
                out var shuttleDock, out var shuttleXform, out var gridDock, out var gridXform))
            return false;

        return CanDock(
            request.ShuttleUid,
            shuttleDockUid,
            shuttleDock,
            shuttleXform,
            gridDock,
            gridXform,
            request.ShuttleAabb,
            request.ShuttleFixtures,
            request.TargetGridEntity,
            request.TargetGridXform,
            request.TargetGridAngle,
            request.Grids,
            request.IsMap,
            coordinates,
            angle,
            coordinates != null,
            out transform, out bounds, out rotation);
    }

    private bool TryResolveQueuedDockPair(
        PendingDockingSearch request,
        EntityUid shuttleDockUid,
        EntityUid gridDockUid,
        out DockingComponent shuttleDock,
        out TransformComponent shuttleXform,
        out DockingComponent gridDock,
        out TransformComponent gridXform)
    {
        shuttleDock = default!;
        shuttleXform = default!;
        gridDock = default!;
        gridXform = default!;
        if (Terminating(shuttleDockUid) || Terminating(gridDockUid))
            return false;

        if (!TryComp<DockingComponent>(shuttleDockUid, out var currentShuttleDock) ||
            !TryComp<DockingComponent>(gridDockUid, out var currentGridDock) ||
            !_xformQuery.TryGetComponent(shuttleDockUid, out var currentShuttleXform) ||
            !_xformQuery.TryGetComponent(gridDockUid, out var currentGridXform))
        {
            return false;
        }

        if (currentShuttleDock.Docked || currentGridDock.Docked ||
            !currentShuttleXform.Anchored || !currentGridXform.Anchored ||
            currentShuttleXform.GridUid != request.ShuttleUid ||
            currentGridXform.GridUid != request.TargetGrid)
        {
            return false;
        }

        if (_tag.HasTag(shuttleDockUid, PreventFtlTag) || _tag.HasTag(gridDockUid, PreventFtlTag) ||
            (currentShuttleDock.DockType & request.DockType) == DockType.None ||
            (currentGridDock.DockType & request.DockType) == DockType.None ||
            (currentShuttleDock.DockType & currentGridDock.DockType) == DockType.None)
        {
            return false;
        }

        if (HasComp<PriorityDockComponent>(gridDockUid) &&
            !HasComp<TransitShuttleComponent>(request.ShuttleUid))
        {
            return false;
        }

        shuttleDock = currentShuttleDock;
        gridDock = currentGridDock;
        shuttleXform = currentShuttleXform;
        gridXform = currentGridXform;
        return true;
    }
}



