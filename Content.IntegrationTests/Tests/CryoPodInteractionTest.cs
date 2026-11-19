using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Medical.Cryogenics;

namespace Content.IntegrationTests.Tests;

public sealed class CryoPodInteractionTest : InteractionTest
{
    [Test]
    public async Task OpensLoneStarCryoPodUi()
    {
        await SpawnTarget("CryoPod");
        await SpawnEntity("APCBasic", SEntMan.GetCoordinates(TargetCoords));
        await RunTicks(5);

        await Activate();
        Assert.That(IsUiOpen(CryoPodUiKey.Key), Is.True);
    }
}
