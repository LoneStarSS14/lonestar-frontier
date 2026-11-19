namespace Content.Server._NF.Salvage.Expeditions;

/// <summary>
///     This event is raised when an expedition spawn job has completed (either successfully or in failure), and informs whether the job was successful or not.
/// </summary>
public sealed class ExpeditionSpawnCompleteEvent : EntityEventArgs
{
    public EntityUid Station;
    public bool Success;
    public ushort MissionIndex;
    public EntityUid MapUid;
    public string EconomyId;
    public bool OpenContract; // LoneStar

    public ExpeditionSpawnCompleteEvent(EntityUid station, bool success, ushort missionIndex, EntityUid mapUid, string economyId, bool openContract)
    {
        Station = station;
        Success = success;
        MissionIndex = missionIndex;
        MapUid = mapUid;
        EconomyId = economyId;
        OpenContract = openContract; // LoneStar
    }
}
