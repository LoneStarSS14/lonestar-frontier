using Content.Shared.Station;
using Robust.Shared.Prototypes;

namespace Content.Client.Station;

public sealed class StationSpawningSystem : SharedStationSpawningSystem
{
    protected override void EquipPdaCartridgesIfPossible(EntityUid entity, List<EntProtoId> encryptionKeys) { }
}
