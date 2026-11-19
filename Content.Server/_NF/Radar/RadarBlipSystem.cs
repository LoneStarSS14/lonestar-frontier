// _CS Start
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared._Goobstation.Vehicles;
using Content.Shared._NF.Radar;
using Content.Shared.GameTicking;
using Content.Shared.Movement.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._NF.Radar;

/// <summary>
/// A system that handles and rate-limits client-made requests for radar blips.
/// </summary>
/// <remarks>
/// Ported from Monolith's RadarBlipsSystem.
/// </remarks>
public sealed partial class RadarBlipSystem : SharedRadarBlipSystem
{
    private readonly record struct ShuttleGridContact(EntityUid GridUid, MapId MapId, Vector2 Position, float Radius);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private Dictionary<NetUserId, TimeSpan> _nextBlipRequestPerUser = new();
    private readonly List<ShuttleGridContact> _cachedShuttleContacts = new();
    private uint _cachedShuttleContactTick = uint.MaxValue;

    // The minimum amount of time between handled blip requests.
    private static readonly TimeSpan MinRequestPeriod = TimeSpan.FromSeconds(1);
    // Maximum distance for blips to be considered visible
    private const float MaxBlipRenderDistance = 300f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RequestBlipsEvent>(OnBlipsRequested);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>
    /// Handles a network request for radar blips and sends the blip data to the requesting client.
    /// </summary>
    private void OnBlipsRequested(RequestBlipsEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetEntity(ev.Radar, out var radarUid))
            return;

        if (!TryComp<RadarConsoleComponent>(radarUid, out var radar))
            return;

        if (_nextBlipRequestPerUser.TryGetValue(args.SenderSession.UserId, out var requestTime) && _timing.RealTime < requestTime)
            return;

        _nextBlipRequestPerUser[args.SenderSession.UserId] = _timing.RealTime + MinRequestPeriod;

        var blips = AssembleBlipsReport((radarUid.Value, radar));

        var giveEv = new GiveBlipsEvent(blips);
        RaiseNetworkEvent(giveEv, args.SenderSession);
    }

    /// <summary>
    /// Clears blip request data between rounds.
    /// </summary>
    public void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _nextBlipRequestPerUser.Clear();
        _cachedShuttleContacts.Clear();
        _cachedShuttleContactTick = uint.MaxValue;
    }

    private void RefreshShuttleContactCacheIfNeeded()
    {
        var curTick = _timing.CurTick.Value;
        if (_cachedShuttleContactTick == curTick)
            return;

        _cachedShuttleContactTick = curTick;
        _cachedShuttleContacts.Clear();

        var shuttleQuery = EntityQueryEnumerator<ShuttleComponent, MapGridComponent, TransformComponent>();
        while (shuttleQuery.MoveNext(out var shuttleGridUid, out var shuttle, out var shuttleGrid, out var shuttleXform))
        {
            if (!shuttle.Enabled)
                continue;

            var shuttlePosition = _xform.GetWorldPosition(shuttleGridUid);
            var shuttleAabb = shuttleGrid.LocalAABB;
            var shuttleRadius = MathF.Max(shuttleAabb.Width, shuttleAabb.Height) * 0.5f;
            _cachedShuttleContacts.Add(new ShuttleGridContact(shuttleGridUid, shuttleXform.MapID, shuttlePosition, shuttleRadius));
        }
    }

    /// <summary>
    /// Gets the nearest valid radar contact distance for a console within a given scan range.
    /// Contacts on the same grid as the console are ignored.
    /// </summary>
    public bool TryGetNearestContactDistance(Entity<RadarConsoleComponent> ent, float scanRange, out float nearestDistance)
    {
        nearestDistance = float.MaxValue;

        if (!TryComp(ent, out TransformComponent? radarXform))
            return false;

        var radarPosition = _xform.GetWorldPosition(ent);
        var radarGrid = radarXform.GridUid;
        var radarMapId = radarXform.MapID;
        var radarRange = MathF.Min(scanRange, MaxBlipRenderDistance);
        var radarRangeSquared = radarRange * radarRange;

        if (radarRange <= 0f)
            return false;

        var found = false;
        var seenContactGrids = new HashSet<EntityUid>();
        var blipQuery = EntityQueryEnumerator<RadarBlipComponent, TransformComponent>();

        while (blipQuery.MoveNext(out var blipUid, out var blip, out var blipXform))
        {
            if (!blip.Enabled || blipXform.MapID != radarMapId)
                continue;

            var blipGrid = blipXform.GridUid;
            if (blip.RequireNoGrid && blipGrid != null)
                continue;

            if (!blip.VisibleFromOtherGrids && blipGrid != radarGrid)
                continue;

            if (blipGrid != null && blipGrid == radarGrid)
                continue;

            if (blipGrid is { } seenGridUid)
                seenContactGrids.Add(seenGridUid);

            var blipPosition = _xform.GetWorldPosition(blipUid);
            var blipOffset = blipPosition - radarPosition;
            var centerDistanceSquared = blipOffset.LengthSquared();

            // For grid-mounted contacts, measure to the contact grid's hull instead of the console point.
            // This makes proximity alerts start when the grid enters the scan circle.
            var contactDistance = 0f;
            if (blipGrid is { } contactGridUid && TryComp<MapGridComponent>(contactGridUid, out var contactGrid))
            {
                var aabb = contactGrid.LocalAABB;
                var gridRadius = MathF.Max(aabb.Width, aabb.Height) * 0.5f;
                var maxCenterDistance = radarRange + gridRadius;
                var maxCenterDistanceSquared = maxCenterDistance * maxCenterDistance;
                if (centerDistanceSquared > maxCenterDistanceSquared)
                    continue;

                var centerDistance = MathF.Sqrt(centerDistanceSquared);
                contactDistance = MathF.Max(0f, centerDistance - gridRadius);
            }
            else
            {
                if (centerDistanceSquared > radarRangeSquared)
                    continue;

                contactDistance = MathF.Sqrt(centerDistanceSquared);
            }

            if (contactDistance > radarRange)
                continue;

            nearestDistance = MathF.Min(nearestDistance, contactDistance);
            found = true;
        }

        // Some inactive shuttle grids do not currently have active radar blips.
        // Include shuttle grids directly so proximity alerts still trigger on hull entry.
        RefreshShuttleContactCacheIfNeeded();
        foreach (var shuttleContact in _cachedShuttleContacts)
        {
            if (shuttleContact.MapId != radarMapId)
                continue;

            if (radarGrid == shuttleContact.GridUid)
                continue;

            if (seenContactGrids.Contains(shuttleContact.GridUid))
                continue;

            var shuttleOffset = shuttleContact.Position - radarPosition;
            var shuttleCenterDistanceSquared = shuttleOffset.LengthSquared();
            var maxShuttleCenterDistance = radarRange + shuttleContact.Radius;
            var maxShuttleCenterDistanceSquared = maxShuttleCenterDistance * maxShuttleCenterDistance;
            if (shuttleCenterDistanceSquared > maxShuttleCenterDistanceSquared)
                continue;

            var shuttleCenterDistance = MathF.Sqrt(shuttleCenterDistanceSquared);
            var shuttleContactDistance = MathF.Max(0f, shuttleCenterDistance - shuttleContact.Radius);

            if (shuttleContactDistance > radarRange)
                continue;

            nearestDistance = MathF.Min(nearestDistance, shuttleContactDistance);
            found = true;
        }

        return found;
    }

    /// <summary>
    /// Assembles a list of radar blips visible to the given radar console.
    /// </summary>
    private List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)> AssembleBlipsReport(Entity<RadarConsoleComponent> ent)
    {
        var blips = new List<(
            NetEntity? Grid,
            Vector2 Position,
            float Scale,
            Color Color,
            RadarBlipShape Shape)>();

        if (!TryComp(ent, out TransformComponent? radarXform))
            return blips;
        var radarPosition = _xform.GetWorldPosition(ent);
        var radarGrid = radarXform.GridUid;
        var radarMapId = radarXform.MapID;
        var radarRange = MathF.Min(ent.Comp.MaxRange, MaxBlipRenderDistance);

        // Non-positive range, nothing to return.
        if (radarRange <= 0)
            return blips;

        var blipQuery = EntityQueryEnumerator<RadarBlipComponent, TransformComponent>();

        while (blipQuery.MoveNext(out var blipUid, out var blip, out var blipXform))
        {
            if (!blip.Enabled)
                continue;

            if (blipXform.MapID != radarMapId)
                continue;

            // Run cheaper grid checks before distance checks
            var blipGrid = blipXform.GridUid;
            if (blip.RequireNoGrid && blipGrid != null)
                continue;

            if (!blip.VisibleFromOtherGrids && blipGrid != radarGrid)
                continue;

            var blipPosition = _xform.GetWorldPosition(blipUid);
            var distance = (blipPosition - radarPosition).Length();
            if (distance > radarRange)
                continue;

            // Convert blip position to grid coords if needed.
            NetEntity? blipNetGrid = null;
            if (blipGrid != null)
            {
                blipNetGrid = GetNetEntity(blipGrid.Value);
                blipPosition = Vector2.Transform(blipPosition, _xform.GetInvWorldMatrix(blipGrid.Value));
            }
            var scale = blip.Scale;
            var shape = blip.Shape;
            var color = blip.RadarColor;
            // {
            //     var ev = new RadarBlipEvent(
            //         color,
            //         shape,
            //         scale,
            //         blip.Enabled);
            //     RaiseLocalEvent(blipUid, ref ev);
            //     scale = ev.ChangeScale ?? scale;
            //     color = ev.ChangeColor ?? color;
            //     shape = ev.ChangeShape ?? shape;
            //     if (ev.ChangeEnabled.HasValue)
            //     {
            //         blip.Enabled = ev.ChangeEnabled.Value;
            //         if (!blip.Enabled)
            //         {
            //             Log.Debug($"Blip {blipUid} skipped: disabled by event.");
            //             continue;
            //         }
            //     }
            // }

            blips.Add((blipNetGrid, blipPosition, scale, color, shape));
        }
        return blips;
    }

    /// <summary>
    /// Configures the radar blip for a jetpack or vehicle entity.
    /// </summary>
    private void SetupRadarBlip(EntityUid uid, Color color, float scale, bool visibleFromOtherGrids = true, bool requireNoGrid = false)
    {
        var blip = EnsureComp<RadarBlipComponent>(uid);
        blip.RadarColor = color;
        blip.Scale = scale;
        blip.VisibleFromOtherGrids = visibleFromOtherGrids;
        blip.RequireNoGrid = requireNoGrid;
    }

    /// <summary>
    /// Configures the radar blip for a vehicle entity.
    /// </summary>
    public void SetupVehicleRadarBlip(Entity<VehicleComponent> uid)
    {
        SetupRadarBlip(uid, Color.Cyan, 1f, true, true);
    }
}
// _CS End
