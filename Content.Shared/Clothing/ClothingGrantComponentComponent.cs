using Robust.Shared.Prototypes;
//goob station start
namespace Content.Shared.Clothing.Components
{
    [RegisterComponent]
    public sealed partial class ClothingGrantComponentComponent : Component
    {
        [DataField("component", required: true)]
        [AlwaysPushInheritance]
        public ComponentRegistry Components { get; private set; } = new();

        [ViewVariables(VVAccess.ReadWrite)]
        public Dictionary<string, bool> Active = new();
    }
}
//goob station end
