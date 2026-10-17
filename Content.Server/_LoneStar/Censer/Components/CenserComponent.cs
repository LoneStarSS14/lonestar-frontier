using Content.Shared.Damage;
using Robust.Shared.Audio;

namespace Content.Server._LoneStar.Censer.Components
{
    [RegisterComponent]
    public sealed partial class CenserComponent : Component
    {
        [DataField("sizzleSound")]
        public SoundSpecifier SizzleSoundPath = new SoundPathSpecifier("/Audio/Effects/lightburn.ogg");

        [DataField("damageOnUntrainedUse", required: true)]
        [ViewVariables(VVAccess.ReadWrite)]
        public DamageSpecifier DamageOnUntrainedUse = default!;
    }
}

