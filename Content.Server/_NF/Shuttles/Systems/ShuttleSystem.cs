// New Frontiers - This file is licensed under AGPLv3
// Copyright (c) 2024 New Frontiers Contributors
// See AGPLv3.txt for details.

using Content.Server._NF.Station.Components;
using Content.Server._WF.Shuttles.Components; // Wayfarer: Autopilot
using Content.Server._WF.Shuttles.Systems; // Wayfarer: Autopilot
using Content.Server.Popups;
using Content.Server._NF.Radar;
using Content.Server.Shuttles.Components;
using Content.Shared._CS;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Audio;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using System.Numerics;
using Content.Server._Coyote.ShuttleCrewStatus;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Physics.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    private const float MinProximityAlertRadius = 1f;
    private const float MinProximityPingInterval = 0.25f;
    private const float MaxProximityPingInterval = 2.5f;
    private const float ProximityPingStepMeters = 30f;
    private const float ProximityInitialIntervalMultiplier = 2f;
    private static readonly TimeSpan ProximityScanInterval = TimeSpan.FromSeconds(0.2f);
    private static readonly TimeSpan ProximityInitialToPingDelay = TimeSpan.FromSeconds(0.5f);
    private static readonly SoundPathSpecifier ProximityInitialSound = new("/Audio/_Mono/Effects/Computer/beep_series1.ogg");
    private static readonly SoundPathSpecifier ProximityPingSound = new("/Audio/_Mono/Effects/Computer/short_beep2.ogg");

    [Dependency] private readonly RadarConsoleSystem _radarConsole = default!;
    [Dependency] private readonly RadarBlipSystem _radarBlips = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly PopupSystem _popupSystem = null!;
    [Dependency] private readonly AutopilotSystem _autopilot = default!; // Wayfarer: Autopilot

    private const float SpaceFrictionStrength = 0.000f; // slip slide
    private const float DampenDampingStrength = 0.25f;
    private const float AnchorDampingStrength = 2.5f;
    private void NfInitialize()
    {
        SubscribeLocalEvent<ShuttleConsoleComponent, SetInertiaDampeningRequest>(OnSetInertiaDampening);
        SubscribeLocalEvent<ShuttleConsoleComponent, SetServiceFlagsRequest>(NfSetServiceFlags);
        SubscribeLocalEvent<ShuttleConsoleComponent, SetTargetCoordinatesRequest>(NfSetTargetCoordinates);
        SubscribeLocalEvent<ShuttleConsoleComponent, SetHideTargetRequest>(NfSetHideTarget);
        SubscribeLocalEvent<ShuttleConsoleComponent, SetProximityAlertRequest>(NfSetProximityAlert);
        SubscribeLocalEvent<ShuttleConsoleComponent, SetProximityAlertRadiusRequest>(NfSetProximityAlertRadius);
    }

    private bool SetInertiaDampening(
        EntityUid uid,
        PhysicsComponent physicsComponent,
        ShuttleComponent shuttleComponent,
        TransformComponent transform,
        InertiaDampeningMode mode)
    {
        if (!transform.GridUid.HasValue)
        {
            return false;
        }

        if (mode == InertiaDampeningMode.Query)
        {
            _console.RefreshShuttleConsoles(transform.GridUid.Value);
            return false;
        }

        if (!EntityManager.HasComponent<ShuttleDeedComponent>(transform.GridUid)
            || EntityManager.HasComponent<StationDampeningComponent>(_station.GetOwningStation(transform.GridUid)))
        {
            return false;
        }

        shuttleComponent.BodyModifier = mode switch
        {
            InertiaDampeningMode.Off => SpaceFrictionStrength,
            InertiaDampeningMode.Dampen => DampenDampingStrength,
            InertiaDampeningMode.Anchor => AnchorDampingStrength,
            _ => DampenDampingStrength, // other values: default to some sane behaviour (assume normal dampening)
        }; // FFS ITS DAMPING

        shuttleComponent.DampingModifier = shuttleComponent.BodyModifier;
        shuttleComponent.EBrakeActive = false;
        _console.RefreshShuttleConsoles(transform.GridUid.Value);
        return true;
    }

    private void OnSetInertiaDampening(
        EntityUid uid,
        ShuttleConsoleComponent component,
        SetInertiaDampeningRequest args)
    {
        // Ensure that the entity requested is a valid shuttle (stations should not be togglable)
        if (!EntityManager.TryGetComponent(uid, out TransformComponent? transform)
            || !transform.GridUid.HasValue
            || !EntityManager.TryGetComponent(transform.GridUid, out PhysicsComponent? physicsComponent)
            || !EntityManager.TryGetComponent(transform.GridUid, out ShuttleComponent? shuttleComponent))
        {
            return;
        }

        // Wayfarer start: Disengage autopilot if pilot manually changes mode
        if (args.Mode != InertiaDampeningMode.Query &&
            TryComp<AutopilotComponent>(transform.GridUid.Value, out var autopilot) &&
            autopilot.Enabled)
        {
            _autopilot.DisableAutopilot(transform.GridUid.Value);
            _autopilot.SendShuttleMessage(transform.GridUid.Value, "Autopilot disengaged - manual mode change");
        }
        // Wayfarer end

        if (SetInertiaDampening(
                uid,
                physicsComponent,
                shuttleComponent,
                transform,
                args.Mode)
            && args.Mode != InertiaDampeningMode.Query)
            component.DampeningMode = args.Mode;
    }

    public InertiaDampeningMode NfGetInertiaDampeningMode(EntityUid entity)
    {
        if (!EntityManager.TryGetComponent<TransformComponent>(entity, out var xform))
            return InertiaDampeningMode.Dampen;

        // Not a shuttle, shouldn't be togglable
        if (!EntityManager.HasComponent<ShuttleDeedComponent>(xform.GridUid)
            || EntityManager.HasComponent<StationDampeningComponent>(_station.GetOwningStation(xform.GridUid)))
            return InertiaDampeningMode.Station;

        if (!EntityManager.TryGetComponent(xform.GridUid, out ShuttleComponent? shuttle))
            return InertiaDampeningMode.Dampen;

        if (shuttle.EBrakeActive)
            return InertiaDampeningMode.Emergency; // mainly to uncheck the thing in the UI

        if (shuttle.BodyModifier >= AnchorDampingStrength)
            return InertiaDampeningMode.Anchor;
        if (shuttle.BodyModifier <= SpaceFrictionStrength)
            return InertiaDampeningMode.Off;
        return InertiaDampeningMode.Dampen;
    }

    public void NfSetPowered(EntityUid uid, ShuttleConsoleComponent component, bool powered)
    {
        // Ensure that the entity requested is a valid shuttle (stations should not be togglable)
        if (!EntityManager.TryGetComponent(uid, out TransformComponent? transform)
            || !transform.GridUid.HasValue
            || !EntityManager.TryGetComponent(transform.GridUid, out PhysicsComponent? physicsComponent)
            || !EntityManager.TryGetComponent(transform.GridUid, out ShuttleComponent? shuttleComponent))
        {
            return;
        }

        // Update dampening physics without adjusting requested mode.
        if (!powered)
        {
            SetInertiaDampening(
                uid,
                physicsComponent,
                shuttleComponent,
                transform,
                InertiaDampeningMode.Anchor);
        }
        else
        {
            // Update our dampening mode if we need to, and if we aren't a station.
            var currentDampening = NfGetInertiaDampeningMode(uid);
            if (currentDampening != InertiaDampeningMode.Station
                && component.DampeningMode != InertiaDampeningMode.Station)
            {
                SetInertiaDampening(
                    uid,
                    physicsComponent,
                    shuttleComponent,
                    transform,
                    component.DampeningMode);
            }
        }
    }

    /// <summary>
    /// Get the current service flags for this grid.
    /// </summary>
    public ServiceFlags NfGetServiceFlags(EntityUid uid)
    {
        var transform = Transform(uid);
        // Get the grid entity from the console transform
        if (!transform.GridUid.HasValue)
            return ServiceFlags.None;

        var gridUid = transform.GridUid.Value;

        // Set the service flags on the IFFComponent.
        if (!EntityManager.TryGetComponent<IFFComponent>(gridUid, out var iffComponent))
            return ServiceFlags.None;

        return iffComponent.ServiceFlags;
    }

    /// <summary>
    /// Set the service flags for this grid.
    /// </summary>
    public void NfSetServiceFlags(EntityUid uid, ShuttleConsoleComponent component, SetServiceFlagsRequest args)
    {
        var transform = Transform(uid);
        // Get the grid entity from the console transform
        if (!transform.GridUid.HasValue)
            return;

        var gridUid = transform.GridUid.Value;

        // Set the service flags on the IFFComponent.
        if (!EntityManager.TryGetComponent<IFFComponent>(gridUid, out var iffComponent))
            return;

        var newFlags = args.ServiceFlags;

        // Interdiction state is mutually exclusive.
        if (newFlags.HasFlag(ServiceFlags.InterdictionsEnabled))
            newFlags &= ~ServiceFlags.InterdictionsDisabled;
        else if (newFlags.HasFlag(ServiceFlags.InterdictionsDisabled))
            newFlags &= ~ServiceFlags.InterdictionsEnabled;

        iffComponent.ServiceFlags = newFlags;

        if (TryComp<ShuttleCrewStatusComponent>(gridUid, out var crewStatus))
            iffComponent.Color = GetInterdictionDisplayColor(newFlags, crewStatus.HasActiveCrew, crewStatus.OriginalColor ?? Color.White);
        else
            iffComponent.Color = GetInterdictionDisplayColor(newFlags, true, Color.White);

        _console.RefreshShuttleConsoles(gridUid);
        Dirty(gridUid, iffComponent);
    }

    private static Color GetInterdictionDisplayColor(ServiceFlags flags, bool hasActiveCrew, Color fallback)
    {
        if (flags.HasFlag(ServiceFlags.InterdictionsEnabled))
        {
            var value = hasActiveCrew ? 1.0f : 0.5f;
            return Color.FromHsv(new Vector4(0.90f, 0.75f, value, 1f));
        }

        if (flags.HasFlag(ServiceFlags.InterdictionsDisabled))
        {
            var value = hasActiveCrew ? 0.70f : 0.40f;
            return Color.FromHsv(new Vector4(0.78f, 0.70f, value, 1f));
        }

        return hasActiveCrew ? fallback : Color.Gray;
    }

    public void NfSetTargetCoordinates(EntityUid uid, ShuttleConsoleComponent component, SetTargetCoordinatesRequest args)
    {
        if (!TryComp<RadarConsoleComponent>(uid, out var radarConsole))
            return;

        var transform = Transform(uid);
        // Get the grid entity from the console transform
        if (!transform.GridUid.HasValue)
            return;

        var gridUid = transform.GridUid.Value;

        _radarConsole.SetTarget((uid, radarConsole), args.TrackedEntity, args.TrackedPosition);
        _radarConsole.SetHideTarget((uid, radarConsole), false); // Force target visibility
        _console.RefreshShuttleConsoles(gridUid);
    }

    public void NfSetHideTarget(EntityUid uid, ShuttleConsoleComponent component, SetHideTargetRequest args)
    {
        if (!TryComp<RadarConsoleComponent>(uid, out var radarConsole))
            return;

        var transform = Transform(uid);
        // Get the grid entity from the console transform
        if (!transform.GridUid.HasValue)
            return;

        var gridUid = transform.GridUid.Value;

        _radarConsole.SetHideTarget((uid, radarConsole), args.Hidden);
        _console.RefreshShuttleConsoles(gridUid);
    }

    public void NfSetProximityAlert(EntityUid uid, ShuttleConsoleComponent component, SetProximityAlertRequest args)
    {
        var transform = Transform(uid);
        if (!transform.GridUid.HasValue)
            return;

        component.ProximityAlertEnabled = args.Enabled;
        component.NextProximityAlert = TimeSpan.Zero;
        component.NextProximityScan = TimeSpan.Zero;
        component.HasProximityContact = false;
        component.CachedNearestProximityDistance = float.MaxValue;
        Dirty(uid, component);
        _console.RefreshShuttleConsoles(transform.GridUid.Value);
    }

    public void NfSetProximityAlertRadius(EntityUid uid, ShuttleConsoleComponent component, SetProximityAlertRadiusRequest args)
    {
        var transform = Transform(uid);
        if (!transform.GridUid.HasValue)
            return;

        // Radius is locked while active to avoid unstable timing updates from rapid slider changes.
        if (component.ProximityAlertEnabled)
            return;

        component.ProximityAlertRadius = MathF.Max(MinProximityAlertRadius, args.Radius);
        component.NextProximityAlert = TimeSpan.Zero;
        component.NextProximityScan = TimeSpan.Zero;
        component.HasProximityContact = false;
        component.CachedNearestProximityDistance = float.MaxValue;
        Dirty(uid, component);
        _console.RefreshShuttleConsoles(transform.GridUid.Value);
    }

    private void NfUpdateProximityAlerts()
    {
        var curTime = _gameTiming.CurTime;
        var consoleQuery = EntityQueryEnumerator<ShuttleConsoleComponent, RadarConsoleComponent>();

        while (consoleQuery.MoveNext(out var uid, out var console, out var radar))
        {
            // Keep alarm behavior active while enabled, even if nobody is currently using the console UI.
            if (!console.ProximityAlertEnabled)
                continue;

            if (console.NextProximityScan == TimeSpan.Zero || curTime >= console.NextProximityScan)
            {
                console.NextProximityScan = curTime + ProximityScanInterval;

                if (_radarBlips.TryGetNearestContactDistance((uid, radar), console.ProximityAlertRadius, out var nearestContactDistance))
                {
                    console.HasProximityContact = true;
                    console.CachedNearestProximityDistance = nearestContactDistance;
                }
                else
                {
                    console.HasProximityContact = false;
                    console.CachedNearestProximityDistance = float.MaxValue;
                    console.NextProximityAlert = TimeSpan.Zero;
                }
            }

            if (!console.HasProximityContact)
                continue;

            var nearestDistance = console.CachedNearestProximityDistance;

            if (console.NextProximityAlert == TimeSpan.Zero)
            {
                var initialParams = AudioParams.Default.WithVolume(-4f).WithMaxDistance(10f);
                _audio.PlayPvs(ProximityInitialSound, uid, initialParams);
                console.NextProximityAlert = curTime + ProximityInitialToPingDelay;
                continue;
            }

            if (console.NextProximityAlert != TimeSpan.Zero && curTime < console.NextProximityAlert)
                continue;

            var interval = CalculateProximityPingInterval(nearestDistance, console.ProximityAlertRadius);
            var audioParams = AudioParams.Default.WithVolume(-4f).WithMaxDistance(10f);
            _audio.PlayPvs(ProximityPingSound, uid, audioParams);
            console.NextProximityAlert = curTime + TimeSpan.FromSeconds(interval);
        }
    }

    private static float CalculateProximityPingInterval(float nearestDistance, float scanRadius)
    {
        _ = scanRadius; // Detection range gate is handled before this call; cadence is distance-based.

        var clampedDistance = MathF.Max(0f, nearestDistance);

        // 30m distance bands from center: each band farther away halves BPM (doubles interval).
        var distanceBands = (int) MathF.Floor(clampedDistance / ProximityPingStepMeters);
        var interval = MinProximityPingInterval * ProximityInitialIntervalMultiplier * MathF.Pow(2f, distanceBands);
        return Math.Clamp(interval, MinProximityPingInterval, MaxProximityPingInterval * ProximityInitialIntervalMultiplier);
    }

    /// <summary>
    /// Throws on the emergency brake for any shuttle that:
    /// Is a player shuttle, AND
    /// Doesn't have anyone in it OR
    /// everyone inside is either in crit or dead OR
    /// The shuttle console is not powered or EMPed
    /// </summary>
    public void ShouldEmergencyBrake()
    {
        var query = EntityQueryEnumerator<ShuttleComponent>();
        var whereIsEveryone = GetPlayerShipsWithPeopleOnThem();

        var consolesByGrid = new Dictionary<EntityUid, HashSet<Entity<ShuttleConsoleComponent>>>();
        var consolesQuery = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consolesQuery.MoveNext(out var consoleUid, out var consoleComp, out var consoleXform))
        {
            if (consoleXform.GridUid is not { } gridUid)
                continue;

            if (!consolesByGrid.TryGetValue(gridUid, out var consoles))
            {
                consoles = new HashSet<Entity<ShuttleConsoleComponent>>();
                consolesByGrid[gridUid] = consoles;
            }

            consoles.Add((consoleUid, consoleComp));
        }

        while (query.MoveNext(out var uid, out var shuttle))
        {
            var curTime = _gameTiming.CurTime;
            if (curTime < shuttle.NextBrakeCheck)
                continue;

            shuttle.NextBrakeCheck = curTime + shuttle.BrakeDelay;

            if (shuttle.EBrakeActive)
            {
                continue;
            }
            if (!shuttle.PlayerShuttle)
            {
                continue;
            }
            if (shuttle.DampingModifier > SpaceFrictionStrength)
            {
                // Its already able to slow down on its own, no need to emergency brake
                continue;
            }
            // If the shuttle is not moving, no need to emergency brake
            if (!TryComp(uid, out PhysicsComponent? gridBody))
            {
                Log.Warning($"Shuttle {ToPrettyString(uid)} does not have a PhysicsComponent!!!");
                continue;
            }
            // if the shuttle is moving under a certain speed, just quietly engage the emergency brake
            var quietly = false;
            var gridVelocity = gridBody.LinearVelocity;
            if (gridVelocity.LengthSquared() < 1f)
            {
                continue; // no need to emergency brake, shuttle is basically stationary
            }
            if (gridVelocity.LengthSquared() < 25f) // 5 squared
            {
                quietly = true; // shuttle is slowly moving, engage the emergency brake quietly
            }

            var mygrid = Transform(uid).GridUid;
            if (mygrid is null)
            {
                continue;
            }

            // is the shuttle present in the list of player ships with people on them?
            if (whereIsEveryone.Contains(mygrid.Value))
            {
                continue; // people are on it, no need to emergency brake
            }

            consolesByGrid.TryGetValue(mygrid.Value, out var cronsoles);
            cronsoles ??= new HashSet<Entity<ShuttleConsoleComponent>>();

            // No people on board and shuttle is moving - engage emergency brake!
            EngageEmergencyBrake(
                uid,
                shuttle,
                cronsoles,
                quietly);
        }
    }

    /// <summary>
    /// Returns a HashSet of:
    /// Shuttle EntityUids where: it is a player shuttle, and players are inside, and at least one player is alive.
    /// </summary>
    private HashSet<EntityUid> GetPlayerShipsWithPeopleOnThem()
    {
        var whereDict = new HashSet<EntityUid>();
        foreach (var sesh in _players.Sessions)
        {
            // Get the player entity
            if (!sesh.AttachedEntity.HasValue)
            {
                continue;
            }
            var attached = sesh.AttachedEntity.Value;
            // If the player is in crit or dead, skip them
            if (!HasComp<AdminGhostComponent>(attached))
            {
                if (!_mobState.IsAlive(attached)
                    || HasComp<GhostComponent>(attached))
                {
                    continue;
                }
            }

            // Get the shuttle the player is on, if any
            if (!EntityManager.TryGetComponent(attached, out TransformComponent? transform)
                || !transform.GridUid.HasValue
                || !TryComp<ShuttleComponent>(transform.GridUid.Value, out var shuttle)
                || !shuttle.PlayerShuttle)
            {
                continue;
            }
            whereDict.Add(transform.GridUid.Value);
        }
        return whereDict;
    }

    /// <summary>
    /// Turns on the emergency brake for a given shuttle.
    /// </summary>
    private void EngageEmergencyBrake(
        EntityUid uid,
        ShuttleComponent shuttle,
        HashSet<Entity<ShuttleConsoleComponent>> consoles,
        bool quietly = false)
    {
        if (shuttle.EBrakeActive)
        {
            return;
        }

        if (!EntityManager.TryGetComponent(uid, out TransformComponent? transform)
            || !transform.GridUid.HasValue
            || !EntityManager.TryGetComponent(transform.GridUid, out PhysicsComponent? physicsComponent))
        {
            return;
        }
        SetInertiaDampening(
            uid,
            physicsComponent,
            shuttle,
            transform,
            InertiaDampeningMode.Anchor);
        shuttle.EBrakeActive = true;
        if (consoles.Count > 0)
        {
            SoundSpecifier eBrakeBeep = quietly switch
            {
                true => new SoundPathSpecifier("/Audio/_CS/ShuttleStuff/ShuttleEBrakeEngagedQuietly.ogg"),
                false => new SoundPathSpecifier("/Audio/_CS/ShuttleStuff/ShuttleEBrakeEngaged.ogg"),
            };
            var audioParams = quietly switch
            {
                true => AudioParams.Default.WithVariation(SharedContentAudioSystem.DefaultVariation).WithVolume(1f).WithMaxDistance(10f),
                false => AudioParams.Default.WithVariation(SharedContentAudioSystem.DefaultVariation).WithVolume(3f).WithMaxDistance(20f),
            };

            foreach (var console in consoles)
            {
                // get the entity the console is attached to
                var consoleEntity = console.Owner;
                _audio.PlayPvs(
                    eBrakeBeep,
                    consoleEntity,
                    audioParams);
                if (!quietly) // throw in a BANG to make it more dramatic
                {
                    _audio.PlayPvs(
                        _shuttleImpactSound,
                        consoleEntity,
                        audioParams.WithVolume(5f));
                }
                if (quietly)
                {
                    _popupSystem.PopupEntity(
                        "Emergency Brake Engaged",
                        consoleEntity,
                        PopupType.MediumCaution);
                }
                else
                {
                    _popupSystem.PopupEntity(
                        "EMERGENCY BRAKE ENGAGED!!",
                        consoleEntity,
                        PopupType.LargeCaution);
                }
            }
        }
    }




}
