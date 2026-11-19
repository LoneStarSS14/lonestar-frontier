using System.Collections.Generic;
using Content.Server.Parallax;
using Content.Server.Salvage.Expeditions;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Parallax.Biomes.Markers;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class SalvageExpeditionReservationTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: biomeMarkerLayer
  id: BiomeReservationTestMarker
  size: 16
  radius: 1
  maxCount: 32
";

    [Test]
    public async Task MarkerNodesSkipReservedExpeditionTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var biomeSystem = entManager.System<BiomeSystem>();
        var testMap = await pair.CreateTestMap();

        var markerTiles = new HashSet<Vector2i>();

        await server.WaitPost(() =>
        {
            var gridUid = testMap.Grid.Owner;
            var grid = entManager.GetComponent<MapGridComponent>(gridUid);
            var biome = entManager.EnsureComponent<BiomeComponent>(gridUid);
            var expedition = entManager.EnsureComponent<SalvageExpeditionComponent>(gridUid);

            expedition.DungeonBounds = new Box2(1f, 1f, 2f, 2f);
            expedition.ReservedLandingZones.Add(new Box2(3f, 3f, 4f, 4f));

            var layer = proto.Index<BiomeMarkerLayerPrototype>("BiomeReservationTestMarker");

            biomeSystem.GetMarkerNodes(
                gridUid,
                biome,
                grid,
                layer,
                false,
                new Box2i(0, 0, 5, 5),
                12,
                new Random(1337),
                out var spawnSet,
                out _);

            markerTiles.UnionWith(spawnSet.Keys);
        });

        Assert.That(markerTiles.Count, Is.GreaterThan(0));
        Assert.That(markerTiles.Contains(new Vector2i(1, 1)), Is.False);
        Assert.That(markerTiles.Contains(new Vector2i(3, 3)), Is.False);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExpeditionShutdownClearsOnlyMatchingDestinationDisks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var expedition = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var otherDestination = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var unrelatedDisk = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var firstDisk = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var secondDisk = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<SalvageExpeditionComponent>(expedition);
            var unrelated = entities.AddComponent<ShuttleDestinationCoordinatesComponent>(unrelatedDisk);
            var first = entities.AddComponent<ShuttleDestinationCoordinatesComponent>(firstDisk);
            var second = entities.AddComponent<ShuttleDestinationCoordinatesComponent>(secondDisk);
            unrelated.Destination = otherDestination;
            first.Destination = expedition;
            second.Destination = expedition;

            entities.RemoveComponent<SalvageExpeditionComponent>(expedition);

            Assert.That(unrelated.Destination, Is.EqualTo(otherDestination));
            Assert.That(first.Destination, Is.Null);
            Assert.That(second.Destination, Is.Null);

            entities.DeleteEntity(unrelatedDisk);
            entities.DeleteEntity(firstDisk);
            entities.DeleteEntity(secondDisk);
            entities.DeleteEntity(otherDestination);
            entities.DeleteEntity(expedition);
        });

        await pair.CleanReturnAsync();
    }
}