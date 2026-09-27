using Robust.Shared.Audio;

namespace Content.Server._LoneStar.RandomSprite;

[RegisterComponent]
public sealed partial class RandomSpriteRerollComponent : Component
{
    [DataField]
    public LocId DenyRerollMessage = "randomsprite-reroll-denyreroll";
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public SoundSpecifier? RerollSound = new SoundPathSpecifier("/Audio/Items/wirecutter.ogg");
}
