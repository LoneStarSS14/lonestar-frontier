using System.Linq;
using System.Numerics;
using Content.Shared.Consent;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._CS.SniffAndSmell;

/// <summary>
/// This handles...
/// </summary>
public sealed class ScentSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedInteractionSystem _interact = default!;
    [Dependency] private readonly IGameTiming _time = default!;
    [Dependency] private readonly IRobustRandom _rng = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedConsentSystem _consent = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public TimeSpan BaseSmellCooldown = TimeSpan.FromSeconds(12);
    public TimeSpan NextSmellDetectionTime = TimeSpan.Zero;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<ScentComponent, ComponentStartup>(OnScentStartup);
        SubscribeLocalEvent<ScentComponent, GetVerbsEvent<InteractionVerb>>(GetSmellVerbs);
        SubscribeLocalEvent<ScentComponent, ExaminedEvent>(OnExamined);
        // SubscribeLocalEvent<ActorComponent, ComponentStartup>(AddSmeller);
    }

    private void OnScentStartup(EntityUid uid, ScentComponent component, ComponentStartup args)
    {
        // load scents from prototypes
        foreach (var scentProtoId in component.ScentPrototypesToAdd)
        {
            if (_proto.TryIndex(scentProtoId, out var scentProto))
            {
                var scentInstance = new Scent(scentProto, Guid.NewGuid().ToString());
                component.Scents.Add(scentInstance);
            }
        }
    }

    /// <summary>
    /// Adds smell verbs to smellers.
    /// </summary>
    private void GetSmellVerbs(EntityUid uid, ScentComponent component, GetVerbsEvent<InteractionVerb> args)
    {
        if (!TryComp<SmellerComponent>(args.User, out SmellerComponent? smellerComp))
            return;
        bool canSmellTarget = TryComp<ScentComponent>(args.Target, out ScentComponent? scentComp);
        // Me smell you (or me)
        if (args.CanInteract
            && canSmellTarget
            && scentComp != null)
        {
            InteractionVerb verb = new()
            {
                Text = "Smell",
                Priority = 2,
                Category = VerbCategory.Actions,
                Disabled = !_interact.InRangeUnobstructed(
                    args.User,
                    args.Target,
                    2f,
                    CollisionGroup.InteractImpassable),
                Act = () =>
                {
                    DirectSmellScent(
                        args.User,
                        args.Target,
                        smellerComp,
                        scentComp);
                }
            };
            args.Verbs.Add(verb);
        }

        // And, the blanket toggle passive detection verb
        if (args.User == args.Target)
        {
            var passiveText = smellerComp.PassiveSmellDetectionEnabled ? "Ignore Scents" : "Notice Scents";
            InteractionVerb passiveVerb = new()
            {
                Text = passiveText,
                Priority = 0,
                Category = VerbCategory.Actions,
                Act = () =>
                {
                    smellerComp.PassiveSmellDetectionEnabled = !smellerComp.PassiveSmellDetectionEnabled;
                    string popupMsg = smellerComp.PassiveSmellDetectionEnabled
                        ? Loc.GetString("scent-verb-passive-unignore-popup")
                        : Loc.GetString("scent-verb-passive-ignore-popup");
                    _popupSystem.PopupEntity(
                        popupMsg,
                        args.User,
                        args.User,
                        PopupType.MediumCautionLingering,
                        false);
                }
            };
            args.Verbs.Add(passiveVerb);
        }
        // and a verb to toggle being able to smell this smelly beast
        // trust me, its needed
        if (args.User != args.Target
            && scentComp != null)
        {
            var isIgnoringThem = IsIgnoringSmell(smellerComp, scentComp);
            var toggleText = isIgnoringThem ? "Notice Scent" : "Ignore Scent";

            InteractionVerb toggleVerb = new()
            {
                Text = toggleText,
                Priority = 1,
                Category = VerbCategory.Actions,
                Act = () =>
                {
                    ToggleIgnoreSmell(smellerComp, scentComp);
                    var popupMsg = isIgnoringThem
                        ? Loc.GetString(
                            "scent-verb-unignore-popup",
                            ("smelly", Identity.Entity(
                                args.Target,
                                EntityManager,
                                args.User)))
                        : Loc.GetString(
                            "scent-verb-ignore-popup",
                            ("smelly", Identity.Entity(
                                args.Target,
                                EntityManager,
                                args.User)));
                    _popupSystem.PopupEntity(
                        popupMsg,
                        args.User,
                        args.User,
                        PopupType.MediumCautionLingering,
                        false);
                }
            };
            args.Verbs.Add(toggleVerb);
        }
    }

    /// <summary>
    /// Checks if the smeller is ignoring the scent of the target.
    /// If all scent instance IDs of the target are in the smeller's ignored list, then they are ignoring the scent.
    /// Otherwise, they are not.
    /// </summary>
    private static bool IsIgnoringSmell(SmellerComponent smeller, ScentComponent scent)
    {
        return scent.Scents.All(x => smeller.IgnoredScentInstanceIds.Contains(x.ScentInstanceId));
    }

    /// <summary>
    /// Toggles ignoring the scent of the target.
    /// </summary>
    private static void ToggleIgnoreSmell(SmellerComponent smeller, ScentComponent scent)
    {
        if (IsIgnoringSmell(smeller, scent))
        {
            smeller.IgnoredScentInstanceIds.RemoveWhere(x => scent.Scents.Any(s => s.ScentInstanceId == x));
        }
        else
        {
            smeller.IgnoredScentInstanceIds.UnionWith(scent.Scents.Select(x => x.ScentInstanceId));
            // also clear any pending smells from this scent
            smeller.PendingSmells.RemoveAll(x => scent.Scents.Any(s => s.ScentInstanceId == x.ScentInstanceId));
        }
    }

    /// <summary>
    /// Handles examining scent sources.
    /// </summary>
    private void OnExamined(EntityUid uid, ScentComponent component, ExaminedEvent args)
    {
        if (component.Scents.Count == 0)
            return;
        List<string> scentDescriptions = new();
        foreach (var scent in component.Scents)
        {
            if (!_proto.TryIndex<ScentPrototype>(scent.ScentProto, out var proto))
                continue; // invalid scent proto
            if (proto.ScentsExamine.Count == 0)
                continue;
            if (!ConsentEnabled(args.Examiner, proto.ConsentToggled))
                continue;
            var smellColor = "slateblue";
            if (proto.Stinky && proto.ConsentToggled)
            {
                smellColor = "Magenta";
            }
            else if (proto.ConsentToggled)
            {
                smellColor = "HotPink";
            }
            else if (proto.Stinky)
            {
                smellColor = "GreenYellow";
            }

            var toAdd = _rng.Pick(proto.ScentsExamine);
            var prestring = Loc.GetString(
                toAdd,
                ("src", Identity.Entity(
                    args.Examined,
                    EntityManager,
                    args.Examiner)));
            prestring = "[color=" + smellColor + "]" + prestring + "[/color]";
            scentDescriptions.Add(prestring);
        }

        if (scentDescriptions.Count == 0)
            return;
        // shuffle descriptions
        _rng.Shuffle(scentDescriptions);
        // combine descriptions
        // "They smell like X, Y, and Z."
        string combinedDesc;
        if (scentDescriptions.Count == 1)
        {
            combinedDesc = Loc.GetString(
                "scent-examine-one",
                ("scenter", Identity.Entity(args.Examined, EntityManager)),
                ("scent", scentDescriptions[0]));
        }
        else if (scentDescriptions.Count == 2)
        {
            combinedDesc = Loc.GetString(
                "scent-examine-two",
                ("scenter", Identity.Entity(args.Examined, EntityManager)),
                ("scent1", scentDescriptions[0]),
                ("scent2", scentDescriptions[1]));
        }
        else
        {
            var allButLast = scentDescriptions.Take(scentDescriptions.Count - 1);
            var last = scentDescriptions.Last();
            combinedDesc = Loc.GetString(
                "scent-examine-multiple",
                ("scenter", Identity.Entity(args.Examined, EntityManager)),
                ("scents", string.Join(", ", allButLast)),
                ("lastscent", last));
        }
        using (args.PushGroup("DanIsCool"))
        {
            // cus its easier to edit colors from code
            combinedDesc = $"[color=aquamarine]{combinedDesc}[/color]";
            args.PushMarkup(combinedDesc, 3); // between Physical and Personality
        }
    }

    // /// <summary>
    // /// Adds a smeller component to actors.
    // /// </summary>
    // private void AddSmeller(EntityUid uid, ActorComponent component, ComponentStartup args)
    // {
    //     EnsureComp<SmellerComponent>(uid);
    // }

    /// <summary>
    /// Adds a scent prototype to a scent component!
    /// </summary>
    public void AddScentPrototype(ScentComponent component, ProtoId<ScentPrototype> scentProtoId)
    {
        if (_proto.TryIndex(scentProtoId, out var scentProto))
        {
            var scentInstance = new Scent(scentProto, Guid.NewGuid().ToString());
            component.Scents.Add(scentInstance);
        }
    }

    /// <inheritdoc/>
    /// <summary>
    /// Does two things:
    /// Gather surrounding scents to be processed later
    /// Process pending smells at set intervals
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (NextSmellDetectionTime > _time.CurTime)
            return;
        NextSmellDetectionTime = _time.CurTime + BaseSmellCooldown;

        var query = EntityQueryEnumerator<SmellerComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!component.PassiveSmellDetectionEnabled)
                continue;
            if (!IsConnectedClient(uid))
                continue;
            DetectSmells(uid, component);
            UpdatePendingSmells(uid, component);
            ProcessPendingSmells(uid, component);
        }
    }

    /// <summary>
    /// Checks if the uid has a connected client.
    /// </summary>
    /// <param name="uid"></param>
    private bool IsConnectedClient(EntityUid uid)
    {
        // todo, this
        return true;
    }

    #region Detecting Smells
    private void DetectSmells(EntityUid uid, SmellerComponent component)
    {
        if (component.NextSmellDetectionTime > _time.CurTime)
            return;
        component.NextSmellDetectionTime = _time.CurTime + component.SmellDetectionInterval;

        var smellerPos = _transform.GetWorldPosition(uid);
        var query = EntityQueryEnumerator<ScentComponent>();
        while (query.MoveNext(out var scentUid, out var scentComp))
        {
            if (scentComp.Scents.Count == 0)
                continue;
            if (scentUid == uid)
                continue; // dont detect self
            var scentEntityPos = _transform.GetWorldPosition(scentUid);
            var scentCoords = _transform.GetMapCoordinates(scentUid);
            var distance = Vector2.Distance(smellerPos, scentEntityPos);
            foreach (var scent in scentComp.Scents)
            {
                if (!_proto.TryIndex<ScentPrototype>(scent.ScentProto, out var scentProto))
                    continue; // invalid scent proto
                if (!CanDetectScent(
                        uid,
                        scentUid,
                        component,
                        scent.ScentProto,
                        scent.ScentInstanceId,
                        scentCoords,
                        distance))
                    continue;

                // create a new smell ticket
                var newScentTicket = new SmellTicket(
                    scentUid,
                    scent.ScentProto,
                    scent.ScentInstanceId,
                    scentCoords,
                    _time.CurTime,
                    distance);
                var newScentProto = newScentTicket.ScentProto;
                UpdatePriority(
                    newScentTicket,
                    newScentProto,
                    distance);
                if (UpdateExistingSmellTicketPriority(component, newScentTicket))
                    continue;
                component.PendingSmells.Add(newScentTicket);
            }
        }
    }

    /// <summary>
    /// Determines if the entity can passively detect the scent based on:
    /// bunch of stuff
    /// </summary>
    private bool CanDetectScent(
        EntityUid uid,
        EntityUid scentSourceUid,
        SmellerComponent component,
        ProtoId<ScentPrototype> scentProtoid,
        string scentGuid,
        MapCoordinates scentCoords,
        float distance)
    {
        if (!_proto.TryIndex(scentProtoid, out var scentProto))
            return false; // invalid scent proto
        // early bounces
        if (distance > scentProto.FarRange)
            return false; // out of range!
        if (scentProto.DetectionChance <= 0)
            return false; // undetectable scent
        if (scentProto.DirectOnly)
            return false; // dont detect non-passive scents
        if (component.IgnoredScentInstanceIds.Contains(scentGuid))
            return false; // ignoring this scent
        // check if we have any components blocking us from smelling this scent
        if (CompBlockSmell(uid, scentSourceUid, scentProto))
            return false;
        // consent check
        if (!ConsentEnabled(uid, scentProto.ConsentToggled))
            return false; // cant detect consent based scents
        if (SmellGuidIsOnCooldown(component, scentGuid))
            return false; // on cooldown
        // check LOS, if required
        if (scentProto.RequireLoS)
        {
            if (!_interact.InRangeUnobstructed(
                    uid,
                    scentCoords,
                    scentProto.FarRange,
                    CollisionGroup.InteractImpassable))
                return false; // no LOS
        }
        return true;
    }

    /// <summary>
    /// Checks if any components block smell between the smeller and scent source.
    /// </summary>
    private bool CompBlockSmell(
        EntityUid smellerUid,
        EntityUid scentSourceUid,
        ScentPrototype scentProto)
    {
        if (scentProto.BlockingComponents.Count > 0)
        {
            if (scentProto.BlockingComponents.Any(blockComp => HasComp(smellerUid, blockComp.Value.Component.GetType())))
            {
                return true; // blocked by component
            }
        }
        // check if they have any components preventing them from emitting this scent
        if (scentProto.PreventingComponents.Count > 0)
        {
            if (scentProto.PreventingComponents.Any(preventComp => HasComp(scentSourceUid, preventComp.Value.Component.GetType())))
            {
                return true; // prevented by component
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if the scent is on cooldown for the smeller.
    /// </summary>
    private bool SmellGuidIsOnCooldown(SmellerComponent component, string scentId)
    {
        if (!component.SmelledScentsCooldowns.TryGetValue(scentId, out var nextSmellTime))
            return false;
        if (nextSmellTime == TimeSpan.Zero)
            return false; // no cooldown set
        // if (component.PendingSmells.Count <= 2)
        //     return false; // not enough pending smells to be on cooldown
        if (nextSmellTime <= _time.CurTime)
            return false;
        return true;
    }

    /// <summary>
    /// Assigns priority to a smell ticket based on scent proto and distance.
    /// </summary>
    private void UpdatePriority(SmellTicket ticket, ProtoId<ScentPrototype> protoid, float dist)
    {
        if (!_proto.TryIndex(protoid, out var proto))
            throw new InvalidOperationException($"Invalid scent prototype ID {protoid} in UpdatePriority.");
        var priority = 1.0;
        if (dist > proto.FarRange)
        {
            // out of range, no priority
            ticket.SetPriority(-999.0);
            return;
        }
        if (dist <= proto.CloseRange)
        {
            // close range scents are higher priority, scaled up by closeness
            // the highers the priority value, the higher priority it is
            // provides a 2.0 addition flat
            // then up to an additional 3.0 based on distance
            var distanceFactor = proto.CloseRange - dist;
            if (distanceFactor < 0.1f)
                distanceFactor = 0.1f;
            priority += 2.0 + 3.0 * distanceFactor / proto.CloseRange;
            // max priority is 6.0 at 0 distance
        }
        else
        {
            // far range scents are lower priority, scaled up by closeness
            // provuidees an additional 2.0 based on distance
            var distanceFactor = proto.FarRange - dist;
            if (distanceFactor < 0.1f)
                distanceFactor = 0.1f;
            priority += (2.0 * distanceFactor / proto.FarRange);
        }

        ticket.SetPriority(priority);
    }

    /// <summary>
    /// Updates an existing smell ticket's priority if it exists.
    /// </summary>
    private bool UpdateExistingSmellTicketPriority(
        SmellerComponent component,
        SmellTicket newTicket)
    {
        // pull any tickets with the same scent instance id to update priority instead of adding new
        var existingTicket = component.PendingSmells
            .FirstOrDefault(x => x?.ScentInstanceId == newTicket.ScentInstanceId, null);
        if (existingTicket == null)
            return false;
        // update priority if higher
        if (newTicket.Priority <= existingTicket.Priority)
            return true; // discard new ticket, we updated existing
        existingTicket.Priority = newTicket.Priority;
        existingTicket.OriginCoordinates = newTicket.OriginCoordinates;
        existingTicket.Distance = newTicket.Distance;
        return true; // discard new ticket, we updated existing
    }
    #endregion

    #region Updating Pending Smells
    private void UpdatePendingSmells(EntityUid uid, SmellerComponent component)
    {
        if (component.PendingSmells.Count == 0)
            return;
        if (component.NextPendingSmellUpdateTime > _time.CurTime)
            return;
        component.NextPendingSmellUpdateTime = _time.CurTime + component.PendingSmellUpdateInterval;

        foreach (var ticket in component.PendingSmells.ToArray())
        {
            if (!_proto.TryIndex<ScentPrototype>(ticket.ScentProto, out var scentProto))
            {
                component.PendingSmells.Remove(ticket);
                continue; // invalid scent proto
            }
            var smellerCoords = _transform.GetMapCoordinates(uid);
            var smellerPos = smellerCoords.Position;
            UpdatePositionAndDistance(uid, ticket);
            if (ticket.Distance > scentProto.FarRange)
                goto RemoveTicket;
            if (ticket.OriginCoordinates.MapId != smellerCoords.MapId)
                goto RemoveTicket;
            // check if we can still smell it
            if (!CanDetectScent(
                    uid,
                    ticket.SourceEntity,
                    component,
                    ticket.ScentProto,
                    ticket.ScentInstanceId,
                    ticket.OriginCoordinates,
                    ticket.Distance))
                goto RemoveTicket;
            // Still valid, update priority
            UpdatePriority(
                ticket,
                ticket.ScentProto,
                ticket.Distance);
            continue;

        RemoveTicket:
            component.PendingSmells.Remove(ticket);
        }
    }
    #endregion

    #region Processing Pending Smells
    private void ProcessPendingSmells(EntityUid uid, SmellerComponent component)
    {
        if (component.NextSmellProcessingTime > _time.CurTime)
            return;
        var interval = _rng.Next(
            component.SmellProcessingTickIntervalRange.X,
            component.SmellProcessingTickIntervalRange.Y);
        component.NextSmellProcessingTime = _time.CurTime + TimeSpan.FromSeconds(interval);

        if (component.PendingSmells.Count == 0)
            return;

        component.PendingSmells.Sort((a, b) => a.ComparePriority(b));

        foreach (var ticket in component.PendingSmells.ToArray())
        {

            if (!CanDetectScent(
                uid,
                ticket.SourceEntity,
                component,
                ticket.ScentProto,
                ticket.ScentInstanceId,
                ticket.OriginCoordinates,
                ticket.Distance))
            {
                component.PendingSmells.Remove(ticket);
                continue;
            }
            if (!_proto.TryIndex<ScentPrototype>(ticket.ScentProto, out var scentProto))
            {
                component.PendingSmells.Remove(ticket);
                continue; // invalid scent proto
            }
            var chance = scentProto.DetectionChance;
            var ticketPriority = ticket.GetPriority(_proto);
            if (ticketPriority > 1.0)
                chance *= (float)ticketPriority;

            bool successfullySmelled = _rng.Prob(Math.Clamp(chance, 0f, 1f));
            if (!successfullySmelled)
                continue;
            SmellScent(
                uid,
                component,
                ticket);
            component.PendingSmells.Remove(ticket);
            break;
        }
    }

    /// <summary>
    /// Updates the position and distance of a smell ticket.
    /// Also determines if the ticket should be removed.
    /// </summary>
    private void UpdatePositionAndDistance(
        EntityUid uid,
        SmellTicket ticket)
    {
        var smellerPos = _transform.GetWorldPosition(uid);
        var scentCoords = _transform.GetMapCoordinates(ticket.SourceEntity);
        ticket.OriginCoordinates = scentCoords;
        var scentPos = ticket.OriginCoordinates.Position;
        var distance = Vector2.Distance(scentPos, smellerPos);
        ticket.Distance = distance;
    }
    #endregion

    #region Smelling
    /// <summary>
    /// Actually smells a thing.
    /// Throws messages, sets cooldowns, etc.
    /// </summary>
    private void SmellScent(
        EntityUid uid,
        SmellerComponent component,
        SmellTicket ticket,
        bool direct = false)
    {
        if (!_proto.TryIndex<ScentPrototype>(ticket.ScentProto, out var proto))
            throw new InvalidOperationException($"Invalid scent prototype ID {ticket.ScentProto} in SmellScent.");
        if (!ConsentEnabled(uid, proto.ConsentToggled))
            return;

        IncurSmellCooldown(component, ticket);
        UpdatePositionAndDistance(uid, ticket);

        // and, if they have a pending smell ticket for this scent, remove it
        // chances are it'll be readded with a new cooldown later
        component.PendingSmells.RemoveAll(x => x.ScentInstanceId == ticket.ScentInstanceId);

        if (proto.ScentsDirect.Count == 0
            && proto.ScentsClose.Count == 0
            && proto.ScentsFar.Count == 0)
            throw new InvalidOperationException($"Scent prototype {proto.ID} has no scent messages defined.");

        // Get the appropriate message list based on range
        List<string>? messages = null;
        if (proto.DirectOnly)
        {
            if (!direct)
                return; // direct only scents cant be smelled passively
        }
        if (direct)
        {
            if (proto.ScentsDirect.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Scent prototype {proto.ID} tried to use direct scent, but no direct scent messages defined!!!");
            }
            messages = proto.ScentsDirect;
        }
        else
        {
            if (ticket.Distance <= proto.CloseRange)
            {
                if (proto.ScentsClose.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Scent prototype {proto.ID} tried to use close scent, but no close scent messages defined!!!");
                }
                messages = proto.ScentsClose;
            }
            else if (ticket.Distance <= proto.FarRange)
            {
                if (proto.ScentsFar.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Scent prototype {proto.ID} tried to use far scent, but no far scent messages defined!!!");
                }
                messages = proto.ScentsFar;
            }
        }

        if (messages == null
            || messages.Count == 0)
        {
            throw new InvalidOperationException(
                $"Scent prototype {proto.ID} has no valid scent messages for the current smelling method/range.");
        }
        // The actual message!

        var smellKind = PopupType.SmallLingering;
        if (proto.Stinky)
        {
            smellKind = PopupType.SmallCautionLingering;
        }

        var locmsg = Loc.GetString(
            _rng.Pick(messages),
            ("src", Identity.Entity(
                ticket.SourceEntity,
                EntityManager,
                uid)));
        _popupSystem.PopupEntity(
            locmsg,
            uid,
            uid,
            smellKind,
            false);
    }

    /// <summary>
    /// Directly smells a scent from a target entity.
    /// Basically sets up a smell ticket and processes it immediately.
    /// </summary>
    private void DirectSmellScent(
        EntityUid smellerUid,
        EntityUid scentUid,
        SmellerComponent smellerComp,
        ScentComponent scentComp)
    {
        // try to find a scent that isnt on cooldown
        List<Scent> availableScents = new();
        foreach (var scent in scentComp.Scents)
        {
            if (!_proto.TryIndex<ScentPrototype>(scent.ScentProto, out var scentProto))
                continue; // invalid scent proto
            // if (SmellGuidIsOnCooldown(smellerComp, scent.ScentInstanceId))
            //     continue;
            if (!ConsentEnabled(smellerUid, scentProto.ConsentToggled))
                continue;
            availableScents.Add(scent);
        }
        // if its empty, just pick any scent
        // if (availableScents.Count == 0)
        // {
        //     availableScents = scentComp.Scents;
        // }
        if (availableScents.Count == 0)
            return; // no scents at all???
        var chosenScent = _rng.Pick(availableScents);
        var scentCoords = _transform.GetMapCoordinates(scentUid);
        var smellerPos = _transform.GetWorldPosition(smellerUid);
        var scentPos = scentCoords.Position;
        var distance = Vector2.Distance(smellerPos, scentPos);
        var directSmellTicket = new SmellTicket(
            scentUid,
            chosenScent.ScentProto,
            chosenScent.ScentInstanceId,
            scentCoords,
            _time.CurTime,
            distance);
        SmellScent(
            smellerUid,
            smellerComp,
            directSmellTicket,
            true);
        if (smellerUid != scentUid)
        {
            // and a popup to who you sniffed, telling them they got sniffed
            var snifferName = Identity.Entity(
                smellerUid,
                EntityManager,
                scentUid);
            var sniffedMsg = Loc.GetString(
                "scent-sniffed-popup",
                ("sniffer", snifferName));
            _popupSystem.PopupEntity(
                sniffedMsg,
                scentUid,
                scentUid,
                PopupType.SmallLingering,
                false);
        }
    }

    /// <summary>
    /// Incurs a smell cooldown on the smeller for the given scent.
    /// </summary>
    private void IncurSmellCooldown(
        SmellerComponent component,
        SmellTicket ticket)
    {
        if (!_proto.TryIndex(ticket.ScentProto, out var scentProto))
            throw new InvalidOperationException($"Invalid scent prototype ID {ticket.ScentProto} in IncurSmellCooldown.");
        // first, remember that we smelled this scent, and set our personal cooldown
        // doesnt factor in here, its for the pending smell tickets
        // you can totally sniff someone any time tho
        double cooldownSeconds = _rng.Next(
            scentProto.MinCooldown,
            scentProto.MaxCooldown);
        var ticketPriority = ticket.GetPriority(_proto);
        if (ticketPriority > 1.0)
        {
            // if the priority was higher than 1.0, reduce the cooldown via priority
            // max reduction of 75% at 6.0 priority
            // inverse square law based reduction
            // simulates people being close to you smelling stronger for longer
            var cdMultiplier = 25.0 / Math.Pow(ticketPriority + 4.0, 2.0);
            cooldownSeconds *= cdMultiplier;
            cooldownSeconds = Math.Clamp(
                cooldownSeconds,
                scentProto.MinCooldown,
                scentProto.MaxCooldown);
        }
        var nextSmellTime = _time.CurTime + TimeSpan.FromSeconds(cooldownSeconds);
        component.SmelledScentsCooldowns[ticket.ScentInstanceId] = nextSmellTime;
    }

    /// <summary>
    /// Consent checks: prevents smelling scents if the user has them disabled.
    /// If the scent isnt consent toggled, then allow it.
    /// Allows admin ghosts.
    /// Otherwise, checl consents
    /// </summary>
    private bool ConsentEnabled(EntityUid uid, bool consent)
    {
        if (!consent)
            return true;
        if (HasComp<AdminGhostComponent>(uid)) // Allow aghosts.
            return true;
        return _consent.HasConsent(uid, "CanSmellConsentToggled");
    }
    #endregion

}
