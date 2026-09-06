using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Linq;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using System.Collections.Generic;

namespace Content.IntegrationTests.Tests.Chemistry
{
    [TestFixture]
    [TestOf(typeof(ReactionPrototype))]
    public sealed class TryAllReactionsTest
    {
        public List<string> ReactionWhitelist = []; // HL: Whitelist as some reactions require cryo beakers, but we don't have a good dynamic check for that yet
        [TestPrototypes]
        private const string Prototypes = @"
- type: entity
  id: TestSolutionContainer
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 50
        canMix: true";

        [Test]
        public async Task TryAllTest()
        {
            await using var pair = await PoolManager.GetServerClient();
            var server = pair.Server;

            var entityManager = server.ResolveDependency<IEntityManager>();
            var prototypeManager = server.ResolveDependency<IPrototypeManager>();
            var testMap = await pair.CreateTestMap();
            var coordinates = testMap.GridCoords;
            var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();

            foreach (var reactionPrototype in prototypeManager.EnumeratePrototypes<ReactionPrototype>())
            {
                if (ReactionWhitelist.Contains(reactionPrototype.ID))
                {
                    Console.WriteLine($"Skipping Reaction {reactionPrototype.ID} as it's in the whitelist");
                    continue;
                }
                //since i have no clue how to isolate each loop assert-wise im just gonna throw this one in for good measure
                Console.WriteLine($"Testing {reactionPrototype.ID}");

                EntityUid beaker = default;
                Entity<SolutionComponent>? solutionEnt = default!;
                Solution solution = null;

                await server.WaitAssertion(() =>
                {
                    beaker = entityManager.SpawnEntity("TestSolutionContainer", coordinates);
                    Assert.That(solutionContainerSystem
                        .TryGetSolution(beaker, "beaker", out solutionEnt, out solution));
                    solutionContainerSystem.SetTemperature(solutionEnt.Value, reactionPrototype.MinimumTemperature); // HL: Heat the container up FIRST, so we don't get weird mixing bullshit
                    foreach (var (id, reactant) in reactionPrototype.Reactants)
                    {
#pragma warning disable NUnit2045
                        Assert.That(solutionContainerSystem
                            .TryAddReagent(solutionEnt.Value, id, reactant.Amount, out var quantity));
                        Assert.That(reactant.Amount, Is.EqualTo(quantity));
#pragma warning restore NUnit2045
                    }

                    if (reactionPrototype.MixingCategories != null)
                    {
                        var dummyEntity = entityManager.SpawnEntity(null, MapCoordinates.Nullspace);
                        var mixerComponent = entityManager.AddComponent<ReactionMixerComponent>(dummyEntity);
                        mixerComponent.ReactionTypes = reactionPrototype.MixingCategories;
                        solutionContainerSystem.UpdateChemicals(solutionEnt.Value, true, mixerComponent);
                    }
                });

                await server.WaitIdleAsync();

                await server.WaitAssertion(() =>
                {
                    //you just got linq'd fool
                    //(i'm sorry)
                    var foundProductsMap = reactionPrototype.Products
                        .Concat(reactionPrototype.Reactants.Where(x => x.Value.Catalyst).ToDictionary(x => x.Key, x => x.Value.Amount))
                        .ToDictionary(x => x, _ => false);
                    foreach (var (reagent, quantity) in solution.Contents)
                    {
                        Assert.That(foundProductsMap.TryFirstOrNull(x => x.Key.Key == reagent.Prototype && x.Key.Value == quantity, out var foundProduct),
                        $"Failed to make Reagent from Reaction: {reactionPrototype.ID}\nBut Got Reagents: {reagent.Prototype} in quantity: {quantity}");
                        foundProductsMap[foundProduct.Value.Key] = true;
                    }

                    Assert.That(foundProductsMap.All(x => x.Value));
                });

            }
            await pair.CleanReturnAsync();
        }
    }

}
