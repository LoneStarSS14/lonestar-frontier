using Robust.Shared.Configuration;

namespace Content.Shared._CS.CCVar;

/// <summary>
/// Contains CVars used by Coyote.
/// </summary>
[CVarDefs]
public sealed class CSCVars
{
    /// <summary>
    /// Max number of items on a belt before we destroy it/warn admins
    /// </summary>
    public static readonly CVarDef<int> ConveyorMaxItemCount =
    CVarDef.Create("conveyor.max_item_count", 200, CVar.SERVERONLY);
    /// <summary>
    /// Max number of items on a belt before we destroy it/warn admins
    /// </summary>
    public static readonly CVarDef<float> ConveyorCleanupIntervalSeconds =
    CVarDef.Create("conveyor.cleanup_interval_seconds", 51f, CVar.SERVERONLY);
    /// <summary>
    /// Base delay (in seconds) before shuttle IFF status visual updates are applied.
    /// Example: if set to 20, color changes wait at least 20 seconds.
    /// </summary>
    public static readonly CVarDef<int> ShuttleCrewUpdateBaseDelaySeconds =
        CVarDef.Create("coyote.shuttle.crew_update_base_delay", 20, CVar.SERVER);

    /// <summary>
    /// Extra randomized delay range (in seconds) added on top of base delay.
    /// </summary>
    public static readonly CVarDef<int> ShuttleCrewUpdateRandomJitterSeconds =
        CVarDef.Create("coyote.shuttle.crew_update_random_jitter", 25, CVar.SERVER);

    /// <summary>
    /// Hex color used when a player shuttle has no active crew.
    /// Default is gray (#808080).
    /// </summary>
    public static readonly CVarDef<string> ShuttleInactiveCrewColor =
        CVarDef.Create("coyote.shuttle.inactive_crew_color", "#808080", CVar.SERVER);

    /// <summary>
    /// Enables verbose logging for shuttle crew status logic.
    /// </summary>
    public static readonly CVarDef<bool> ShuttleCrewStatusDebug =
        CVarDef.Create("coyote.shuttle.crew_status_debug", false, CVar.SERVER);

}
