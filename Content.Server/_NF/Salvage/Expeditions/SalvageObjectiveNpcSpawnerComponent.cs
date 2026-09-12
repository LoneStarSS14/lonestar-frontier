using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._NF.Salvage.Expeditions;

/// <summary>
/// Periodically spawns faction-themed NPCs from a salvage objective structure while
/// enforcing a local population cap.
/// </summary>
[RegisterComponent]
[AutoGenerateComponentPause]
public sealed partial class SalvageObjectiveNpcSpawnerComponent : Component
{
    [DataField(required: true)]
    public List<EntProtoId> SpawnPrototypes = new();

    [DataField(required: true)]
    public HashSet<ProtoId<NpcFactionPrototype>> NearbyFactions = new();

    /// <summary>
    /// Time in seconds between each attempted spawning.
    /// </summary>
    [DataField]
    public float SpawnIntervalSeconds = 75;

    /// <summary>
    /// A value added or subtracted with a random scale from 0-1 to SpawnIntervalSeconds.
    /// </summary>
    [DataField]
    public float SpawnIntervalVariance = 10f;

    /// <summary>
    /// The distance an actor/player must be from the spawner to allow it to spawn an entity on a given tile.
    /// Distance is a square radius.
    /// </summary>
    [DataField]
    public float NearbyActorRange = 11f;

    /// <summary>
    /// The distance to search for nearby faction members when attempting to spawn more.
    /// Distance is a circle radius.
    /// </summary>
    [DataField]
    public float NearbyRange = 20;

    /// <summary>
    /// The distance to look for candidate tiles to spawn on.
    /// Distance is a square radius.
    /// </summary>
    [DataField]
    public float SpawnRange = 20;

    /// <summary>
    /// The maximum amount of faction members allowed nearby when attempting to spawn more.
    /// There must be LESS than this number.
    /// </summary>
    [DataField]
    public int MaxNearby = 5;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSpawn = TimeSpan.Zero;
}
