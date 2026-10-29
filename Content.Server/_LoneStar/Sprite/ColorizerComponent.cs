using Robust.Shared.Audio;

namespace Content.Server._LoneStar.Sprite;

[RegisterComponent]
public sealed partial class ColorizerComponent : Component
{
    public EntityUid? Target;

    [DataField]
    public LocId DenyRerollMessage = "colorizer-denyreroll";
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public SoundSpecifier? RerollSound = new SoundPathSpecifier("/Audio/Items/wirecutter.ogg");
}
