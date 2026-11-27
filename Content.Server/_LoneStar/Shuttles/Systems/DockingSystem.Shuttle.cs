// LoneStar: heavily modified this file.

using System.Linq;
using System.Numerics;
using Content.Server._NF.PublicTransit.Components;
using Content.Server.Shuttles;
using Content.Server.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Content.Shared.Shuttles.Components; // Frontier

namespace Content.Server._LoneStar.Shuttles.Systems;

public sealed partial class DockingSystem
{
    /*
     * Handles the shuttle side of FTL docking.
     */

    private const int DockRoundingDigits = 2;
    private const string PreventFtlTag = "PreventFTL";

    // Avoid repeating expensive spawn validation while evaluating dock combinations.
    private readonly Dictionary<(EntityUid Shuttle, EntityUid Grid, Matrix3x2 Transform, Angle Angle, Vector2 DockingSide, bool IgnoreSpawnPadding), bool>
        _validSpawnCache = new();

    public Angle GetAngle(EntityUid uid, TransformComponent xform, EntityUid targetUid, TransformComponent targetXform)
    {
        var (shuttlePos, shuttleRot) = _transform.GetWorldPositionRotation(xform);
        var (targetPos, targetRot) = _transform.GetWorldPositionRotation(targetXform);

        var shuttleCOM = Robust.Shared.Physics.Transform.Mul(new Transform(shuttlePos, shuttleRot),
            _physicsQuery.GetComponent(uid).LocalCenter);
        var targetCOM = Robust.Shared.Physics.Transform.Mul(new Transform(targetPos, targetRot),
            _physicsQuery.GetComponent(targetUid).LocalCenter);

        var mapDiff = shuttleCOM - targetCOM;
        var angle = mapDiff.ToWorldAngle();
        angle -= targetRot;
        return angle;
    }

    /// <summary>
    /// Checks if 2 docks can be connected by moving the shuttle directly onto docks.
    /// </summary>
    private bool CanDock(
        EntityUid shuttleUid,
        EntityUid shuttleDockUid,
        DockingComponent shuttleDock,
        TransformComponent shuttleDockXform,
        DockingComponent gridDock,
        TransformComponent gridDockXform,
        Box2 shuttleAABB,
        FixturesComponent shuttleFixtures,
        Entity<MapGridComponent> gridEntity,
        TransformComponent targetGridXform,
        Angle targetGridAngle,
        List<Entity<MapGridComponent>> grids,
        bool isMap,
        EntityCoordinates? requestedCoordinates,
        Angle? requestedAngle,
        bool exactOnly,
        out Matrix3x2 matty,
        out Box2 shuttleDockedAABB,
        out Angle gridRotation)
    {
        shuttleDockedAABB = Box2.UnitCentered;
        gridRotation = Angle.Zero;
        matty = Matrix3x2.Identity;

        if (shuttleDock.Docked ||
            gridDock.Docked ||
            !shuttleDockXform.Anchored ||
            !gridDockXform.Anchored)
        {
            return false;
        }

        var dockingDirection = shuttleDockXform.LocalRotation.RotateVec(new Vector2(0f, -1f));
        GetDockTransform(shuttleDockXform, gridDockXform, out matty, out var offsetAngle);

        if (exactOnly &&
            (requestedCoordinates is null ||
             requestedAngle is null ||
             !new EntityCoordinates(gridEntity.Owner, Vector2.Transform(Vector2.Zero, matty))
                 .Equals(requestedCoordinates.Value) ||
             !offsetAngle.Reduced().EqualsApprox(requestedAngle.Value, 0.15)))
        {
            return false;
        }

        shuttleDockedAABB = matty.TransformBox(shuttleAABB);

        // Reject placements overlapping another grid before doing detailed fixture validation.
        var gridPosition = new EntityCoordinates(gridEntity.Owner, Vector2.Transform(Vector2.Zero, matty));
        var spawnPosition = new EntityCoordinates(
            targetGridXform.MapUid!.Value,
            _transform.ToMapCoordinates(gridPosition).Position);
        var targetWorldAngle = (targetGridAngle + offsetAngle).Reduced();
        var dockedBounds = new Box2Rotated(
            shuttleAABB.Translated(spawnPosition.Position),
            targetWorldAngle,
            spawnPosition.Position);

        grids.Clear();
        _mapManager.FindGridsIntersecting(
            targetGridXform.MapID,
            dockedBounds,
            ref grids,
            includeMap: false);

        if (grids.Any(grid => grid.Owner != gridEntity.Owner && grid.Owner != targetGridXform.MapUid))
            return false;

        // Avoid repeating expensive spawn validation for identical dock transforms.
        if (!IsValidSpawnCached(
                shuttleUid,
                gridEntity,
                matty,
                offsetAngle,
                dockingDirection,
                shuttleFixtures,
                isMap,
                HasComp<TransitShuttleComponent>(shuttleUid) ||
                HasComp<PriorityDockComponent>(shuttleDockUid)))
            return false;

        gridRotation = offsetAngle.Reduced();
        return true;
    }

    private static void GetDockTransform(
        TransformComponent shuttleDockXform,
        TransformComponent gridDockXform,
        out Matrix3x2 transform,
        out Angle offsetAngle)
    {
        var stationDockPos = shuttleDockXform.LocalPosition +
                             shuttleDockXform.LocalRotation.RotateVec(new Vector2(0f, -1f));
        var shuttleDockAngle = shuttleDockXform.LocalRotation;
        var gridDockAngle = gridDockXform.LocalRotation.Opposite();
        offsetAngle = gridDockAngle - shuttleDockAngle;

        var stationDockMatrix = Matrix3Helpers.CreateInverseTransform(stationDockPos, shuttleDockAngle);
        var gridXformMatrix = Matrix3Helpers.CreateTransform(gridDockXform.LocalPosition, gridDockAngle);
        transform = Matrix3x2.Multiply(stationDockMatrix, gridXformMatrix);
    }

    /// <summary>
    /// Gets docking config between 2 specific docks.
    /// </summary>
    public DockingConfig? GetDockingConfig(
        EntityUid shuttleUid,
        EntityUid targetGrid,
        EntityUid shuttleDockUid,
        DockingComponent shuttleDock,
        EntityUid gridDockUid,
        DockingComponent gridDock)
    {
        var shuttleDocks = new List<Entity<DockingComponent>>(1)
       {
           (shuttleDockUid, shuttleDock)
       };

        var gridDocks = new List<Entity<DockingComponent>>(1)
       {
           (gridDockUid, gridDock)
       };

        return GetDockingConfigPrivate(shuttleUid, targetGrid, shuttleDocks, gridDocks);
    }

    /// <summary>
    /// Tries to get a valid docking configuration for the shuttle to the target grid.
    /// </summary>
    /// <param name="priorityTag">Priority docking tag to prefer, e.g. for emergency shuttle</param>
    public DockingConfig? GetDockingConfig(
        EntityUid shuttleUid,
        EntityUid targetGrid,
        string? priorityTag = null,
        DockType dockType = DockType.Airlock)
    {
        var gridDocks = GetDocks(targetGrid);
        var shuttleDocks = GetDocks(shuttleUid);

        return GetDockingConfigPrivate(
            shuttleUid,
            targetGrid,
            shuttleDocks,
            gridDocks,
            priorityTag,
            dockType);
    }

    /// <summary>
    /// Tries to get a docking config at the specified coordinates and angle.
    /// </summary>
    public DockingConfig? GetDockingConfigAt(EntityUid shuttleUid,
        EntityUid targetGrid,
        EntityCoordinates coordinates,
        Angle angle,
        bool fallback = true,
        DockType dockType = DockType.Airlock,
        string? priorityTag = null) // Frontier
    {
        var gridDocks = GetDocks(targetGrid);
        var shuttleDocks = GetDocks(shuttleUid);

        // First search for the exact requested docking configuration.
        var configs = GetDockingConfigs(
            shuttleUid,
            targetGrid,
            shuttleDocks,
            gridDocks,
            dockType,
            coordinates,
            angle,
            exactOnly: true);

        if (configs.Count > 0)
        {
            if (priorityTag is not null &&
                configs.FirstOrDefault(config => IsConfigPriority(config, priorityTag)) is { } priorityConfig)
            {
                return priorityConfig;
            }

            return configs.First();
        }

        if (!fallback)
            return null;

        if (priorityTag is not null)
            return GetDockingConfigPrivate(shuttleUid, targetGrid, shuttleDocks, gridDocks, priorityTag, dockType);

        configs = GetDockingConfigs(shuttleUid, targetGrid, shuttleDocks, gridDocks, dockType); // Frontier: add dockType
        if (configs.Count > 0)
        {
            return configs.First();
        }

        return null;
    }

    /// <summary>
    /// Gets all docking configs between the 2 grids.
    /// </summary>
    private List<DockingConfig> GetDockingConfigs(
        EntityUid shuttleUid,
        EntityUid targetGrid,
        List<Entity<DockingComponent>> shuttleDocks,
        List<Entity<DockingComponent>> gridDocks,
        DockType dockType,
        EntityCoordinates? requestedCoordinates = null,
        Angle? requestedAngle = null,
        bool exactOnly = false)
    {
        var validDockConfigs = new List<DockingConfig>();
        _validSpawnCache.Clear();
        if (gridDocks.Count <= 0)
            return validDockConfigs;

        // Exclude unusable docks before entering the nested combination loops.
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
            return validDockConfigs;

        var shuttleDockTransforms = shuttleDocks.ToDictionary(
            dock => dock.Owner,
            dock => _xformQuery.GetComponent(dock.Owner));
        var gridDockTransforms = gridDocks.ToDictionary(
            dock => dock.Owner,
            dock => _xformQuery.GetComponent(dock.Owner));

        var targetGridGrid = _gridQuery.GetComponent(targetGrid);
        var targetGridXform = _xformQuery.GetComponent(targetGrid);
        var targetGridAngle = _transform.GetWorldRotation(targetGridXform).Reduced();
        var shuttleFixturesComp = Comp<FixturesComponent>(shuttleUid);
        var shuttleAABB = _gridQuery.GetComponent(shuttleUid).LocalAABB;

        var isMap = HasComp<MapComponent>(targetGrid);

        var grids = new List<Entity<MapGridComponent>>();
        // We'll try all combinations of shuttle docks and see which one is most suitable.
        foreach (var (dockUid, shuttleDock) in shuttleDocks)
        {
            var shuttleDockXform = shuttleDockTransforms[dockUid];

            foreach (var (gridDockUid, gridDock) in gridDocks)
            {
                var gridXform = gridDockTransforms[gridDockUid];

                if (!CanDock(
                            shuttleUid,
                            dockUid,
                            shuttleDock, shuttleDockXform,
                            gridDock, gridXform,
                            shuttleAABB,
                            shuttleFixturesComp,
                            (targetGrid, targetGridGrid),
                            targetGridXform,
                            targetGridAngle,
                            grids,
                            isMap,
                            requestedCoordinates,
                            requestedAngle,
                            exactOnly,
                            out var matty,
                            out var dockedAABB,
                            out var targetAngle))
                {
                    continue;
                }

                var gridPosition = new EntityCoordinates(targetGrid, Vector2.Transform(Vector2.Zero, matty));

                    // Alright well the spawn is valid now to check how many we can connect
                    // Get the matrix for each shuttle dock and test it against the grid docks to see
                    // if the connected position / direction matches.

                var dockedPorts = new List<(EntityUid DockAUid, EntityUid DockBUid, DockingComponent DockA, DockingComponent DockB)>()
                   {
                       (dockUid, gridDockUid, shuttleDock, gridDock),
                   };

                dockedAABB = dockedAABB.Rounded(DockRoundingDigits);

                foreach (var (otherUid, other) in shuttleDocks)
                {
                        if (other == shuttleDock)
                            continue;

                    foreach (var (otherGridUid, otherGrid) in gridDocks)
                    {
                            if (otherGrid == gridDock)
                                continue;

                        if (!CanDock(
                                    shuttleUid,
                                    otherUid,
                                    other,
                                    shuttleDockTransforms[otherUid],
                                    otherGrid,
                                    gridDockTransforms[otherGridUid],
                                    shuttleAABB,
                                    shuttleFixturesComp,
                                    (targetGrid, targetGridGrid),
                                    targetGridXform,
                                    targetGridAngle,
                                    grids,
                                    isMap,
                                     null,
                                     null,
                                     false,
                                    out _,
                                    out var otherdockedAABB,
                                    out var otherTargetAngle))
                        {
                            continue;
                        }

                        otherdockedAABB = otherdockedAABB.Rounded(DockRoundingDigits);

                            // Different setup.
                        if (!targetAngle.Equals(otherTargetAngle) ||
                                !dockedAABB.Equals(otherdockedAABB))
                        {
                            continue;
                        }

                        dockedPorts.Add((otherUid, otherGridUid, other, otherGrid));
                    }
                }

                validDockConfigs.Add(new DockingConfig()
                {
                    Docks = dockedPorts,
                    Coordinates = gridPosition,
                    Area = dockedAABB,
                    Angle = targetAngle,
                });
            }
        }

        return validDockConfigs;
    }

    private DockingConfig? GetDockingConfigPrivate(
        EntityUid shuttleUid,
        EntityUid targetGrid,
        List<Entity<DockingComponent>> shuttleDocks,
        List<Entity<DockingComponent>> gridDocks,
        string? priorityTag = null,
        DockType dockType = DockType.Airlock) // Frontier
    {
        if (priorityTag is null)
        {
            foreach (var dock in shuttleDocks)
            {
                if (TryComp<PriorityDockComponent>(dock.Owner, out var priority) &&
                    priority.Tag is not null)
                {
                    priorityTag = priority.Tag;
                    break;
                }
            }
        }

        List<DockingConfig> validDockConfigs;

        // Map priority docks first so normal docks do not add unnecessary
        // spawn-validation and docking-combination work when a priority dock is available.
        if (priorityTag is not null)
        {
            var priorityGridDocks = gridDocks
                .Where(dock => IsPriorityDock(dock.Owner, priorityTag))
                .ToList();

            validDockConfigs = priorityGridDocks.Count > 0
                ? GetDockingConfigs(
                    shuttleUid,
                    targetGrid,
                    shuttleDocks,
                    priorityGridDocks,
                    dockType)
                : [];

            // Fall back to every available port if the priority ports cannot be used.
            if (validDockConfigs.Count == 0)
                validDockConfigs = GetDockingConfigs(
                    shuttleUid,
                    targetGrid,
                    shuttleDocks,
                    gridDocks,
                    dockType);
        }
        else
        {
            validDockConfigs = GetDockingConfigs(shuttleUid, targetGrid, shuttleDocks, gridDocks, dockType);
        }

        if (validDockConfigs.Count <= 0)
            return null;

        var targetGridAngle = _transform.GetWorldRotation(targetGrid).Reduced();

        // Prefer priority docks, then the maximum connected ports, then the most similar angle.
        // Select the best configuration in one pass instead of sorting every configuration.
        var location = validDockConfigs[0];
        var locationPriority = IsConfigPriority(location, priorityTag);
        var locationAngle = Math.Abs(Angle.ShortestDistance(location.Angle.Reduced(), targetGridAngle).Theta);

        for (var index = 1; index < validDockConfigs.Count; index++)
        {
            var candidate = validDockConfigs[index];
            var candidatePriority = IsConfigPriority(candidate, priorityTag);
            var candidateAngle = Math.Abs(Angle.ShortestDistance(candidate.Angle.Reduced(), targetGridAngle).Theta);

            if (candidatePriority != locationPriority)
            {
                if (candidatePriority)
                {
                    location = candidate;
                    locationPriority = true;
                    locationAngle = candidateAngle;
                }

                continue;
            }

            if (candidate.Docks.Count > location.Docks.Count ||
                candidate.Docks.Count == location.Docks.Count && candidateAngle < locationAngle)
            {
                location = candidate;
                locationAngle = candidateAngle;
            }
        }

        location.TargetGrid = targetGrid;
        // TODO: Ideally do a hyperspace warpin, just have it run on like a 10 second timer.

        return location;
    }

    public List<Entity<DockingComponent>> GetDocks(EntityUid uid)
    {
        _dockingSet.Clear();
        _lookup.GetChildEntities(uid, _dockingSet);

        return _dockingSet.ToList();
    }
}
