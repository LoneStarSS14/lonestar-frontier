using Robust.Shared.Serialization.TypeSerializers.Implementations.Generic;

namespace Content.Server.Speech.Components
{
    [RegisterComponent]
    public sealed partial class StutteringAccentComponent : Component
    {
        [DataField(customTypeSerializer: typeof(DictionarySerializer<int, float>))] // Lonestar: This replaces the old match/three/four bloat.
        public Dictionary<int, float> Probabilities = new()
        {
            [1] = 0.8f,
            [2] = 0.2f,
            [3] = 0.1f,
        };

        /// <summary>
        /// Percentage chance that a stutter cut off.
        /// </summary>
        [DataField("cutRandomProb")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float CutRandomProb = 0.05f;
    }
}
