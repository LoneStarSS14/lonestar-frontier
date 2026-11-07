using Robust.Shared.Serialization;

namespace Content.Shared.Sprite;

public abstract class SharedRandomSpriteSystem : EntitySystem { }
public abstract class SharedColorableClothingSystem : EntitySystem { } // LoneStar

[Serializable, NetSerializable]
public sealed class RandomSpriteColorComponentState : ComponentState
{
    public Dictionary<string, (string State, Color? Color)> Selected = default!;
}
// LoneStar start
[Serializable, NetSerializable]
public sealed class ColorableClothingColorComponentState : ComponentState
{
    public Dictionary<string, (string State, Color? Color)> Selected = default!;
}
// LoneStar end
