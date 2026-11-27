using System.Linq;
using System.Numerics;
using Content.Server._NF.PublicTransit.Components;
using Content.Server.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;

namespace Content.Server._LoneStar.Shuttles.Systems;

public sealed partial class DockingSystem
{
    /// <summary>
    /// Checks whether the shuttle can warp to the specified position.
    /// </summary>
    private bool ValidSpawn(
        EntityUid shuttleUid,
        Entity<MapGridComponent> gridEntity,
        Matrix3x2 transformMatrix,
        Angle angle,
        Vector2 dockingSide,
        FixturesComponent shuttleFixtures,
        bool isMap,
        bool ignoreSpawnPadding)
    {
        var transform = new Transform(
            Vector2.Transform(Vector2.Zero, transformMatrix),
            angle);

        foreach (var fixture in shuttleFixtures.Fixtures.Values)
        {
            if (fixture.Shape is not PolygonShape polygon)
                continue;

            var aabb = polygon.ComputeAABB(transform, 0);
            if (!HasComp<TransitShuttleComponent>(shuttleUid) && !ignoreSpawnPadding)
            {
                var dockingDirection = Vector2.TransformNormal(dockingSide, transformMatrix);
                const float spawnPadding = 0.01f;

                if (Math.Abs(dockingDirection.X) >= Math.Abs(dockingDirection.Y))
                {
                    aabb = dockingDirection.X >= 0
                        ? new Box2(new Vector2(aabb.BottomLeft.X - spawnPadding, aabb.BottomLeft.Y), aabb.TopRight)
                        : new Box2(aabb.BottomLeft, new Vector2(aabb.TopRight.X + spawnPadding, aabb.TopRight.Y));
                }
                else
                {
                    aabb = dockingDirection.Y >= 0
                        ? new Box2(new Vector2(aabb.BottomLeft.X, aabb.BottomLeft.Y - spawnPadding), aabb.TopRight)
                        : new Box2(aabb.BottomLeft, new Vector2(aabb.TopRight.X, aabb.TopRight.Y + spawnPadding));
                }
            }

            if (!isMap)
            {
                if (_mapSystem
                    .GetLocalTilesIntersecting(gridEntity.Owner, gridEntity.Comp, aabb)
                    .Any())
                {
                    return false;
                }

                continue;
            }

            var tiles = _mapSystem.GetLocalTilesEnumerator(
                gridEntity.Owner,
                gridEntity.Comp,
                aabb);

            while (tiles.MoveNext(out var tile))
            {
                var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(
                    gridEntity.Owner,
                    gridEntity.Comp,
                    tile.GridIndices);

                while (anchored.MoveNext(out var entity))
                {
                    if (_physicsQuery.TryGetComponent(entity, out var physics) &&
                        physics.CanCollide &&
                        physics.Hard)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private bool IsValidSpawnCached(
        EntityUid shuttleUid,
        Entity<MapGridComponent> gridEntity,
        Matrix3x2 transform,
        Angle angle,
        Vector2 dockingSide,
        FixturesComponent shuttleFixtures,
        bool isMap,
        bool ignoreSpawnPadding)
    {
        var key = (shuttleUid, gridEntity.Owner, transform, angle, dockingSide, ignoreSpawnPadding);

        if (_validSpawnCache.TryGetValue(key, out var valid))
            return valid;

        valid = ValidSpawn(
            shuttleUid,
            gridEntity,
            transform,
            angle,
            dockingSide,
            shuttleFixtures,
            isMap,
            ignoreSpawnPadding);
        _validSpawnCache[key] = valid;
        return valid;
    }
}
