using System.Collections.Generic;
using Content.Server.Station.Systems;
using Content.Shared._LoneStar.Sprite; // LoneStar
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths; // LoneStar
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Preferences;

[TestFixture]
public sealed class LoadoutTests
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: playTimeTracker
  id: PlayTimeLoadoutTester

# LoneStar start
- type: playTimeTracker
  id: PlayTimeColorLoadoutTester
# LoneStar end

- type: loadout
  id: TestJumpsuit
  equipment:
    jumpsuit: ClothingUniformJumpsuitColorGrey

# LoneStar start
- type: loadout
  id: TestColorableJumpsuit
  tags: [Colorable]
  equipment:
    jumpsuit: ClothingUniformBaseJumpskirtSchool

- type: loadout
  id: TestGreyColorableJumpsuit
  tags: [Colorable]
  equipment:
    jumpsuit: ClothingUniformJumpsuitColorGrey

- type: loadout
  id: TestPunkJumpsuit
  tags: [Colorable]
  equipment:
    jumpsuit: ClothingUniformRandomPunkCroptop
# LoneStar end

- type: loadoutGroup
  id: LoadoutTesterJumpsuit
  name: generic-unknown
  loadouts:
  - TestJumpsuit

- type: roleLoadout
  id: JobLoadoutTester
  groups:
  - LoadoutTesterJumpsuit

# LoneStar start
- type: loadoutGroup
  id: LoadoutTesterColorableJumpsuit
  name: generic-unknown
  loadouts:
  - TestColorableJumpsuit
  - TestGreyColorableJumpsuit
  - TestPunkJumpsuit

- type: roleLoadout
  id: JobLoadoutColorTester
  groups:
  - LoadoutTesterColorableJumpsuit
# LoneStar end

- type: job
  id: LoadoutTester
  playTimeTracker: PlayTimeLoadoutTester

# LoneStar start
- type: job
  id: LoadoutColorTester
  playTimeTracker: PlayTimeColorLoadoutTester
# LoneStar end
";

    private readonly Dictionary<string, EntProtoId> _expectedEquipment = new()
    {
        ["jumpsuit"] = "ClothingUniformJumpsuitColorGrey"
    };

    /// <summary>
    /// Checks that an empty loadout still spawns with default gear and not naked.
    /// </summary>
    [Test]
    public async Task TestEmptyLoadout()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings()
        {
            Dirty = true,
        });
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();

        // Check that an empty role loadout spawns gear
        var stationSystem = entManager.System<StationSpawningSystem>();
        var inventorySystem = entManager.System<InventorySystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var profile = new HumanoidCharacterProfile();

            profile.SetLoadout(new RoleLoadout("LoadoutTester"));

            var tester = stationSystem.SpawnPlayerMob(testMap.GridCoords, job: "LoadoutTester", profile, station: null);

            var slotQuery = inventorySystem.GetSlotEnumerator(tester);
            var checkedCount = 0;
            while (slotQuery.NextItem(out var item, out var slot))
            {
                // Make sure the slot is valid
                Assert.That(_expectedEquipment.TryGetValue(slot.Name, out var expectedItem), $"Spawned item in unexpected slot: {slot.Name}");

                // Make sure that the item is the right one
                var meta = entManager.GetComponent<MetaDataComponent>(item);
                Assert.That(meta.EntityPrototype.ID, Is.EqualTo(expectedItem.Id), $"Spawned wrong item in slot {slot.Name}!");

                checkedCount++;
            }
            // Make sure the number of items is the same
            Assert.That(checkedCount, Is.EqualTo(_expectedEquipment.Count), "Number of items does not match expected!");

            entManager.DeleteEntity(tester);
        });

        await pair.CleanReturnAsync();
    }

    // LoneStar start, verify color and variant selection in loadout spawning.
    [Test]
    public async Task TestColorableLoadoutSelectedColors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var stationSystem = entManager.System<StationSpawningSystem>();
        var inventorySystem = entManager.System<InventorySystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var profile = new HumanoidCharacterProfile();
            var roleLoadout = new RoleLoadout("JobLoadoutColorTester");
            roleLoadout.SelectedLoadouts["LoadoutTesterColorableJumpsuit"] =
            [
                new Loadout
                {
                    Prototype = "TestColorableJumpsuit",
                    InitialColors = new Dictionary<string, Color>
                    {
                        ["Shirt"] = Color.FromHex("#263f73"),
                        ["Dress"] = Color.FromHex("#96bbe5"),
                        ["Ribbon"] = Color.FromHex("#A93A1C"),
                    },
                },
            ];
            profile.SetLoadout(roleLoadout);

            var player = stationSystem.SpawnPlayerMob(testMap.GridCoords, "LoadoutColorTester", profile, station: null);
            Assert.That(inventorySystem.TryGetSlotEntity(player, "jumpsuit", out var uniform), Is.True);

            var initialColors = entManager.GetComponent<ColorableClothingInitialColorsComponent>(uniform!.Value);
            var colorable = entManager.GetComponent<ColorableClothingComponent>(uniform.Value);

            Assert.Multiple(() =>
            {
                Assert.That(initialColors.MappedColors["Shirt"], Is.EqualTo(Color.FromHex("#263f73")));
                Assert.That(colorable.Selected["i-base"].Color, Is.EqualTo(Color.FromHex("#263f73")));
                Assert.That(colorable.Selected["i-detail"].Color, Is.EqualTo(Color.FromHex("#96bbe5")));
                Assert.That(colorable.Selected["i-accent"].Color, Is.EqualTo(Color.FromHex("#A93A1C")));
            });

            entManager.DeleteEntity(player);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestSelectedColorsPersistAndSpawn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var stationSystem = entManager.System<StationSpawningSystem>();
        var inventorySystem = entManager.System<InventorySystem>();
        var testMap = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var roleLoadout = new RoleLoadout("JobLoadoutColorTester");
            roleLoadout.SelectedLoadouts["LoadoutTesterColorableJumpsuit"] =
            [
                new Loadout
                {
                    Prototype = "TestColorableJumpsuit",
                    InitialColors = new Dictionary<string, Color>
                    {
                        ["Shirt"] = Color.FromHex("#123456"),
                    },
                },
            ];

            var profile = new HumanoidCharacterProfile().WithLoadout(roleLoadout);
            Assert.That(
                profile.Loadouts["JobLoadoutColorTester"].SelectedLoadouts["LoadoutTesterColorableJumpsuit"][0].InitialColors["Shirt"],
                Is.EqualTo(Color.FromHex("#123456")));

            var player = stationSystem.SpawnPlayerMob(testMap.GridCoords, "LoadoutColorTester", profile, station: null);
            Assert.That(inventorySystem.TryGetSlotEntity(player, "jumpsuit", out var uniform), Is.True);

            var colorable = entManager.GetComponent<ColorableClothingComponent>(uniform!.Value);
            Assert.That(colorable.Selected["i-base"].Color, Is.EqualTo(Color.FromHex("#123456")));

            entManager.DeleteEntity(player);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestSelectedVariantsApplyWhenSpawningLoadout()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var stationSystem = entManager.System<StationSpawningSystem>();
        var inventorySystem = entManager.System<InventorySystem>();
        var testMap = await pair.CreateTestMap();
        const string selectedVariant = "decor_base_torso_croptop_armless_02";

        await server.WaitAssertion(() =>
        {
            var roleLoadout = new RoleLoadout("JobLoadoutColorTester");
            roleLoadout.SelectedLoadouts["LoadoutTesterColorableJumpsuit"] =
            [
                new Loadout
                {
                    Prototype = "TestPunkJumpsuit",
                    InitialVariants = new Dictionary<string, string>
                    {
                        ["jumpsuit_decor"] = selectedVariant,
                    },
                },
            ];

            var player = stationSystem.SpawnPlayerMob(testMap.GridCoords, "LoadoutColorTester",
                new HumanoidCharacterProfile().WithLoadout(roleLoadout), station: null);
            Assert.That(inventorySystem.TryGetSlotEntity(player, "jumpsuit", out var uniform), Is.True);

            var initialSelection = entManager.GetComponent<ColorableClothingInitialColorsComponent>(uniform!.Value);
            var colorable = entManager.GetComponent<ColorableClothingComponent>(uniform.Value);

            Assert.Multiple(() =>
            {
                Assert.That(initialSelection.SelectedVariants["jumpsuit_decor"], Is.EqualTo(selectedVariant));
                Assert.That(colorable.Selected["jumpsuit_decor"].State, Is.EqualTo(selectedVariant));
            });

            entManager.DeleteEntity(player);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TestMappedColorsApplyToNonRandomColorableClothing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var stationSystem = entManager.System<StationSpawningSystem>();
        var inventorySystem = entManager.System<InventorySystem>();
        var testMap = await pair.CreateTestMap();
        var plumDark = Color.FromHex("#551E4B");

        await server.WaitAssertion(() =>
        {
            var roleLoadout = new RoleLoadout("JobLoadoutColorTester");
            roleLoadout.SelectedLoadouts["LoadoutTesterColorableJumpsuit"] =
            [
                new Loadout
                {
                    Prototype = "TestGreyColorableJumpsuit",
                    InitialColors = new Dictionary<string, Color>
                    {
                        ["Base"] = plumDark,
                    },
                },
            ];

            var profile = new HumanoidCharacterProfile().WithLoadout(roleLoadout);
            var player = stationSystem.SpawnPlayerMob(testMap.GridCoords, "LoadoutColorTester", profile, station: null);
            Assert.That(inventorySystem.TryGetSlotEntity(player, "jumpsuit", out var uniform), Is.True);

            var colorable = entManager.GetComponent<ColorableClothingComponent>(uniform!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(colorable.Selected["uniform-icon"].Color, Is.EqualTo(plumDark));
                Assert.That(colorable.Selected["uniform-equipped"].Color, Is.EqualTo(plumDark));
                Assert.That(colorable.Selected["hl-color"].Color, Is.EqualTo(plumDark));
                Assert.That(colorable.Selected["hr-color"].Color, Is.EqualTo(plumDark));
            });

            entManager.DeleteEntity(player);
        });

        await pair.CleanReturnAsync();
    }
    // LoneStar end
}
