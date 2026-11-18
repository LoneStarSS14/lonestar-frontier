using Content.Shared._CS;
using Content.Shared._NF.CryoSleep;
using Content.Shared.Administration.Logs;
using Content.Shared.Bed.Sleep;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Medical.Cryogenics;
using Content.Shared.Mind.Components;
using Content.Shared.Station;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.SSDIndicator;

/// <summary>
///     Handle changing player SSD indicator status
/// </summary>
public sealed class SSDIndicatorSystem : EntitySystem
{
    public static readonly EntProtoId StatusEffectSSDSleeping = "StatusEffectSSDSleeping";

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLog = default!;
    [Dependency] private readonly SharedStationSystem _stationSystem = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;

    private bool _icSsdSleep;
    private float _icSsdSleepTime;
    private float _jobReopenMinutes = 15f;

    private TimeSpan _updateInterval = TimeSpan.FromSeconds(10);
    private TimeSpan _nextUpdateTime = TimeSpan.Zero;

    public override void Initialize()
    {
        SubscribeLocalEvent<SSDIndicatorComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<SSDIndicatorComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<SSDIndicatorComponent, MapInitEvent>(OnMapInit);

        _cfg.OnValueChanged(
            CCVars.ICSSDSleep,
            obj => _icSsdSleep = obj,
            true);
        _cfg.OnValueChanged(
            CCVars.ICSSDSleepTime,
            obj => _icSsdSleepTime = obj,
            true);
        _cfg.OnValueChanged(
            CCVars.ICSSDJobReopenMinutes,
            obj => _jobReopenMinutes = obj,
            true);
    }

    private void OnPlayerAttached(EntityUid uid, SSDIndicatorComponent component, PlayerAttachedEvent args)
    {
        WipeBraindeadStatus(uid, component);
        component.IsSSD = false;

        // Removes force sleep and resets the time to zero
        if (_icSsdSleep)
        {
            component.FallAsleepTime = TimeSpan.Zero;
            _statusEffects.TryRemoveStatusEffect(uid, StatusEffectSSDSleeping);
        }
        Dirty(uid, component);
    }

    private void OnPlayerDetached(EntityUid uid, SSDIndicatorComponent component, PlayerDetachedEvent args)
    {
        WipeBraindeadStatus(uid, component);
        component.IsSSD = true;
        component.WentBraindeadAt = _timing.CurTime;

        // Sets the time when the entity should fall asleep
        if (_icSsdSleep)
        {
            component.FallAsleepTime = _timing.CurTime + TimeSpan.FromSeconds(_icSsdSleepTime);
        }
        Dirty(uid, component);
    }

    // Prevents mapped mobs to go to sleep immediately
    private void OnMapInit(EntityUid uid, SSDIndicatorComponent component, MapInitEvent args)
    {
        if (_icSsdSleep
            && component.IsSSD
            && component.FallAsleepTime == TimeSpan.Zero)
        {
            component.FallAsleepTime = _timing.CurTime + TimeSpan.FromSeconds(_icSsdSleepTime);
        }
    }

    private void WipeBraindeadStatus(EntityUid uid, SSDIndicatorComponent component)
    {
        component.WentBraindeadAt = TimeSpan.Zero;
        component.BraindeadNashTime = TimeSpan.Zero;
    }

    public bool IsActuallySsd(EntityUid uid, SSDIndicatorComponent component)
    {
        _playerManager.TryGetSessionByEntity(uid, out var session);
        return session?.Status != SessionStatus.InGame;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdateTime)
            return;
        _nextUpdateTime = _timing.CurTime + _updateInterval;

        var query = EntityQueryEnumerator<SSDIndicatorComponent>();

        while (query.MoveNext(out var uid, out var ssd))
        {
            if (!IsActuallySsd(uid, ssd))
            {
                WipeBraindeadStatus(uid, ssd);
                continue;
            }
            // Forces the entity to sleep when the time has come
            if(ssd.IsSSD)
            {
                HandleReopenJob(uid, ssd);
                HandleNashCryoSleep(uid, ssd);
            }
        }
    }

    private void HandleReopenJob(EntityUid uid, SSDIndicatorComponent comp)
    {
        if (!comp.IsSSD
            || !HasHadMind(uid)
            || comp.JobOpened
            || comp.WentBraindeadAt == TimeSpan.Zero)
            return;
        var curTime = _timing.CurTime;
        if (curTime < comp.WentBraindeadAt + TimeSpan.FromMinutes(_jobReopenMinutes))
            return;
        var ev = new SSDJobReopenEvent(uid);
        RaiseLocalEvent(uid, ev);
        comp.JobOpened = true;
    }

    private void HandleNashCryoSleep(EntityUid uid, SSDIndicatorComponent comp)
    {
        if (!comp.IsSSD
            || !HasHadMind(uid)
            || !IsInNashStation(uid)
            || InCryoSleep(uid))
        {
            comp.BraindeadNashTime = TimeSpan.Zero; // Reset the spree time if they are not SSD
            return;
        }
        if (comp.BraindeadNashTime == TimeSpan.Zero)
        {
            comp.BraindeadNashTime = _timing.CurTime; // Start the spree time
            return;
        }
        var curTime = _timing.CurTime;
        if (curTime < comp.BraindeadNashTime + comp.CryoBraindeadTimeLimit)
            return;
        CryoThem(uid, comp);
        comp.BraindeadNashTime = TimeSpan.Zero; // Reset the spree time
    }

    /// <summary>
    /// Check if the entity is in that big station thing where everyone spawns.
    /// okay it just checks if the grid has the CryoBraindeadsComponent
    /// </summary>
    public bool IsInNashStation(EntityUid uid)
    {
        var myGrit = Transform(uid).GridUid;
        if (myGrit == null)
            return false;
        return HasComp<CryoBraindeadsComponent>(myGrit.Value);
    }

    private bool InCryoSleep(EntityUid uid)
    {
        if (!TryComp<MindContainerComponent>(uid, out var mindContainer))
            return false;
        return mindContainer.IsInCryosleep;
    }

    private void CryoThem(EntityUid uid, SSDIndicatorComponent ssd)
    {
        if (InCryoSleep(uid))
            return; // jobs done~
        var cryopod = GetAnCryopodInGrid(uid);
        if (cryopod == null)
            return; // no cryopod, no cryosleep
        var ev = new ForceCryoSleepEvent(uid, cryopod.Value);
        RaiseLocalEvent(
            cryopod.Value,
            ev,
            false);
        var timeNashBraindead = _timing.CurTime - ssd.BraindeadNashTime;
        if (TryComp<MindContainerComponent>(uid, out var mindContainer))
        {
            if (mindContainer.IsInCryosleep)
            {
                _adminLog.Add(
                    LogType.Respawn,
                    LogImpact.Low,
                    $"{ToPrettyString(uid):player} was cryoslept in a cryopod {ToPrettyString(cryopod.Value):cryo} on Nash Station after being braindead for {ssd.BraindeadNashTime.TotalMinutes.ToString("F1") ?? "unknown"} minutes.");
            }
            else
            {
                _adminLog.Add(
                    LogType.Respawn,
                    LogImpact.Low,
                    $"{ToPrettyString(uid):player} failed to be cryoslept in a cryopod {ToPrettyString(cryopod.Value):cryo} on Nash Station after being braindead for {timeNashBraindead.TotalMinutes.ToString("F1") ?? "unknown"} minutes. Should try again soon!");
            }
        }
        else
        {
            _adminLog.Add(
                LogType.Respawn,
                LogImpact.Extreme,
                $"{ToPrettyString(uid):player} was attempted to be cryoslept in a cryopod {ToPrettyString(cryopod.Value):cryo} on Nash Station after being braindead for {timeNashBraindead.TotalMinutes.ToString("F1") ?? "unknown"} minutes, but they have no mind container. They shouldnt have even been considered for cryosleep, wtf");
        }
    }

    private EntityUid? GetAnCryopodInGrid(EntityUid uid)
    {
        var grid = Transform(uid).GridUid;
        if (grid == null)
            return null;
        var cryoQuery = EntityQueryEnumerator<CryoSleepComponent>();
        while (cryoQuery.MoveNext(out var cryoUid, out var cryo))
        {
            if (Transform(cryoUid).GridUid == grid)
                return cryoUid;
        }
        return null;
    }

    private bool HasHadMind(EntityUid uid)
    {
        if (!TryComp<MindContainerComponent>(uid, out var mindContainer))
            return false;
        return mindContainer.HasHadMind;
    }
}

/// <summary>
/// Just tells the job system to try to reopen the job.
/// </summary>
public sealed class SSDJobReopenEvent : EntityEventArgs
{
    public EntityUid User { get; set; }

    public SSDJobReopenEvent(EntityUid user)
    {
        User = user;
    }
}

/// <summary>
/// Raised to shove someone into cryosleep.
/// </summary>
public sealed class ForceCryoSleepEvent : EntityEventArgs
{
    public EntityUid User { get; set; }
    public EntityUid Cryopod { get; set; }
    public bool Handled { get; set; } = false;

    public ForceCryoSleepEvent(EntityUid user, EntityUid cryopod)
    {
        User = user;
        Cryopod = cryopod;
    }
}
