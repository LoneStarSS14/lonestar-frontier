using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Mind.Components;
using Content.Shared.NPC.Components;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Ghost;
using Content.Shared.Physics;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server.Spawners.Components;

namespace Content.Server._NF.Salvage.Expeditions;

/// <summary>
/// Drives <see cref="SalvageObjectiveNpcSpawnerComponent"/> objective structure spawning.
/// </summary>
public sealed class SalvageObjectiveNpcSpawnerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly AnchorableSystem _anchorable = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SalvageObjectiveNpcSpawnerComponent, MapInitEvent>(OnMapInit);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SalvageObjectiveNpcSpawnerComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (Paused(uid) || comp.SpawnPrototypes.Count == 0 || comp.NextSpawn > now)
                continue;

            comp.NextSpawn += TimeSpan.FromSeconds(comp.SpawnIntervalSeconds + (2 * comp.SpawnIntervalVariance * (_random.NextFloat() - 0.5)));

            if (CountNearbyFactionMobs(uid, comp) >= comp.MaxNearby) // Try again soon if too many nearby already
            {
                comp.NextSpawn = TimeSpan.FromSeconds(Math.Min(10, comp.SpawnIntervalSeconds));
                continue;
            }
            var spawn = _random.Pick(comp.SpawnPrototypes);

            if (TryGetNearbySpawnCoordinates(uid, comp, out var coords))
                SpawnAtPosition(spawn, coords);
            else
                SpawnAtPosition(spawn, Transform(uid).Coordinates);
        }
    }

    private void OnMapInit(Entity<SalvageObjectiveNpcSpawnerComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextSpawn = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.SpawnIntervalSeconds);
    }

    private bool HasNearbyActivePlayer(MapCoordinates mapCoords, float range)
    {
        var nearbyActors = new HashSet<Entity<ActorComponent>>();
        var rangeVector = new Vector2(range);
        var bounds = new Box2(mapCoords.Position - rangeVector, mapCoords.Position + rangeVector);
        _lookup.GetEntitiesIntersecting(mapCoords.MapId, bounds, nearbyActors);

        foreach (var nearby in nearbyActors)
        {
            if (!HasComp<GhostComponent>(nearby)
                && TryComp<MindContainerComponent>(nearby, out var mind)
                && mind.HasMind)
                return true;
        }
        return false;
    }

    private int CountNearbyFactionMobs(EntityUid uid, SalvageObjectiveNpcSpawnerComponent comp)
    {
        var xform = Transform(uid);
        var mapCoords = _xforms.GetMapCoordinates((uid, xform));
        var count = 0;

        foreach (var nearby in _lookup.GetEntitiesInRange<NpcFactionMemberComponent>(mapCoords, comp.NearbyRange))
        {
            if (nearby.Owner == uid)
                continue;

            if (!comp.NearbyFactions.Overlaps(nearby.Comp.Factions))
                continue;

            count++;
        }

        return count;
    }

    private bool TryGetNearbySpawnCoordinates(EntityUid uid, SalvageObjectiveNpcSpawnerComponent comp, out EntityCoordinates coords)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { Valid: true } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            coords = default;
            return false;
        }

        var centerTile = _map.CoordinatesToTile(gridUid, grid, _xforms.GetMapCoordinates((uid, xform)));
        var tileRange = Math.Max(1, (int)MathF.Ceiling(comp.SpawnRange));
        var candidates = new List<Vector2i>();

        for (var x = -tileRange; x <= tileRange; x++)
        {
            for (var y = -tileRange; y <= tileRange; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                var offset = new Vector2(x, y);
                if (offset.Length() > comp.SpawnRange)
                    continue;

                candidates.Add(centerTile + new Vector2i(x, y));
            }
        }
        // Iterates through candidate tiles for limitations
        while (candidates.Count > 0)
        {
            var index = _random.Next(candidates.Count);
            var tile = candidates[index];
            candidates.RemoveAt(index);
            // Tile cannot be in reserved landing zones
            if (IsReservedLandingZoneTile(gridUid, grid, tile))
                continue;
            // Tile must be inside (weather flag)
            var tileRef = _map.GetTileRef(gridUid, grid, tile);
            if (!tileRef.Tile.IsEmpty)
            {
                var tileDef = (ContentTileDefinition)_tileDefManager[tileRef.Tile.TypeId];
                if (tileDef.Weather)
                    continue;
            }
            // Tile must not be too close to a player
            var candidateCoords = _map.GridTileToLocal(gridUid, grid, tile);
            if (HasNearbyActivePlayer(_xforms.ToMapCoordinates(candidateCoords), comp.NearbyActorRange))
                continue;

            // Tile must not have an anchored object
            if (!_anchorable.TileFree((gridUid, grid), tile, (int)CollisionGroup.MachineLayer, (int)CollisionGroup.MachineLayer))
                continue;

            coords = candidateCoords;
            return true;
        }

        coords = default;
        return false;
    }

    private bool IsReservedLandingZoneTile(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var mapUid = Transform(gridUid).MapUid;
        if (mapUid is not { Valid: true } || !TryComp<ExpeditionAtmosphereExclusionComponent>(mapUid, out var exclusion))
            return false;

        var tileSize = grid.TileSize;
        var localMin = new Vector2(tile.X * tileSize, tile.Y * tileSize);
        var localMax = new Vector2((tile.X + 1) * tileSize, (tile.Y + 1) * tileSize);
        var worldBox = _xforms.GetWorldMatrix(gridUid).TransformBox(new Box2(localMin, localMax));

        foreach (var zone in exclusion.ExcludedZones)
        {
            if (zone.Intersects(worldBox))
                return true;
        }

        return false;
    }
    // _CS End: salvage objective nearby NPC spawn placement
}
