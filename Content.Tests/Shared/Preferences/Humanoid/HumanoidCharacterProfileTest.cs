using Content.Shared.Preferences;
using NUnit.Framework;

namespace Content.Tests.Shared.Preferences.Humanoid;

[TestFixture]
public sealed class HumanoidCharacterProfileTest
{
    [Test]
    public void EqualsComparesDistinctProfiles()
    {
        var profile = new HumanoidCharacterProfile();
        var equal = new HumanoidCharacterProfile { Appearance = profile.Appearance };
        var different = new HumanoidCharacterProfile { Name = "Another Name" };

        Assert.That(profile.Equals((object) equal), Is.True);
        Assert.That(profile.Equals((object) different), Is.False);
        Assert.That(profile.Equals(null), Is.False);
    }
}
