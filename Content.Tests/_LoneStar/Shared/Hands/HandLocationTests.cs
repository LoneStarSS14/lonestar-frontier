using Content.Shared._NF.Silicons.Borgs;
using Content.Shared.Hands.Components;
using NUnit.Framework;

namespace Content.Tests.Shared.Hands;

[TestFixture]
public class HandLocationTests
{
    public void BorgModuleHandLocationUsesMiddleSlot()
    {
        Assert.That(DroppableBorgModuleSystem.GetModuleHandLocation(), Is.EqualTo(HandLocation.Middle));
    }

    [Test]
    public void BorgModuleHandsAreMarkedAsNonSwappable()
    {
        var moduleHand = new Hand("nf-borg-item-0", HandLocation.Middle, isModule: true);
        var normalHand = new Hand("left", HandLocation.Left);

        Assert.That(moduleHand.IsModule, Is.True);
        Assert.That(moduleHand.IsSwappable, Is.True);
        Assert.That(normalHand.IsModule, Is.False);
        Assert.That(normalHand.IsSwappable, Is.True);
    }

    [Test]
    public void MiddleNonModuleHandsAreNotSwappable()
    {
        var middleHand = new Hand("middle", HandLocation.Middle);

        Assert.That(middleHand.IsSwappable, Is.False);
    }
}
