using System.Collections.Generic;
using System.IO;
using Content.Shared._LoneStar.Sprite;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using YamlDotNet.RepresentationModel;

namespace Content.IntegrationTests.Tests.Sprite;

[TestFixture]
public sealed class ColorableClothingInitialColorsTest
{
    [Test]
    public async Task AppliesMappedColorsFromDataFieldOnMapInit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entityManager = server.EntMan;
        var serialization = server.ResolveDependency<ISerializationManager>();
        var uid = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            using var reader = new StringReader(
                "mappedColors:\n  uniform: \"#123456\"\n");
            var yaml = new YamlStream();
            yaml.Load(reader);
            var data = new MappingDataNode((YamlMappingNode)yaml.Documents[0].RootNode);
            var overrides = serialization.Read<ColorableClothingInitialColorsComponent>(data);

            uid = entityManager.CreateEntityUninitialized(null, MapCoordinates.Nullspace);
            var clothing = entityManager.AddComponent<ColorableClothingComponent>(uid);
            clothing.MappedColors.Add("uniform", "ChatNames");
            clothing.Available.Add(new Dictionary<string, Dictionary<string, string?>>
            {
                ["equipped-INNERCLOTHING"] = new()
                {
                    ["default"] = "uniform",
                },
            });
            entityManager.AddComponent(uid, overrides);

            entityManager.InitializeAndStartEntity(uid, doMapInit: true);

            Assert.That(overrides.MappedColors, Does.ContainKey("uniform"));
            Assert.That(clothing.Selected["equipped-INNERCLOTHING"].Color, Is.EqualTo(Color.FromHex("#123456")));
        });

        await pair.CleanReturnAsync();
    }
}
