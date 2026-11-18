using Content.Server.Chat.Systems;
using Content.Server.Instruments;
using Content.Server.Speech;
using Content.Server.Speech.Components;
using Content.Shared.Chat;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Midi;
using Robust.Shared.Player;
using static Content.Server.Chat.Systems.ChatSystem;

namespace Content.Server.SurveillanceCamera;

public sealed class SurveillanceCameraMicrophoneSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;

    // _CS Start: emote and MIDI relay expansion

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SurveillanceCameraMicrophoneComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SurveillanceCameraMicrophoneComponent, ListenEvent>(RelayEntityMessage);
        SubscribeLocalEvent<SurveillanceCameraMicrophoneComponent, ListenAttemptEvent>(CanListen);
        SubscribeLocalEvent<ExpandICChatRecipientsEvent>(OnExpandRecipients);
        SubscribeLocalEvent<SurveillanceCameraEmoteMessageEvent>(OnEmoteMessage);
        SubscribeLocalEvent<InstrumentMidiPlayedEvent>(OnInstrumentMidiPlayed);
        SubscribeLocalEvent<InstrumentChannelFilterChangedEvent>(OnInstrumentChannelFilterChanged);
    }

    private void OnInstrumentChannelFilterChanged(InstrumentChannelFilterChangedEvent args)
    {
        if (args.Channel < 0 || args.Channel >= RobustMidiEvent.MaxChannels)
            return;

        var uid = args.Source;
        if (HasComp<SurveillanceCameraSpeakerComponent>(uid) || HasComp<SurveillanceCameraMonitorComponent>(uid))
            return;

        if (!TryComp(uid, out TransformComponent? sourceXform))
            return;

        var xformQuery = GetEntityQuery<TransformComponent>();

        foreach (var (microphone, camera, xform) in EntityQuery<SurveillanceCameraMicrophoneComponent, SurveillanceCameraComponent, TransformComponent>())
        {
            if (camera.ActiveMonitors.Count == 0)
                continue;

            if (xform.MapID != sourceXform.MapID)
                continue;

            var cameraPos = _xforms.GetWorldPosition(xform, xformQuery);
            var sourcePos = _xforms.GetWorldPosition(uid, xformQuery);

            if ((sourcePos - cameraPos).Length() > microphone.Range)
                continue;

            var ev = new SurveillanceCameraMidiChannelFilterSyncEvent(uid, args.Channel, args.Filtered);
            foreach (var monitor in camera.ActiveMonitors)
            {
                RaiseLocalEvent(monitor, ev);
            }
        }
    }

    private void OnInstrumentMidiPlayed(InstrumentMidiPlayedEvent args)
    {
        if (args.MidiEvents.Length == 0)
            return;

        var uid = args.Source;
        if (HasComp<SurveillanceCameraSpeakerComponent>(uid) || HasComp<SurveillanceCameraMonitorComponent>(uid))
            return;

        if (!TryComp(uid, out TransformComponent? sourceXform))
            return;

        var xformQuery = GetEntityQuery<TransformComponent>();

        foreach (var (microphone, camera, xform) in EntityQuery<SurveillanceCameraMicrophoneComponent, SurveillanceCameraComponent, TransformComponent>())
        {
            if (camera.ActiveMonitors.Count == 0)
                continue;

            if (xform.MapID != sourceXform.MapID)
                continue;

            var cameraPos = _xforms.GetWorldPosition(xform, xformQuery);
            var sourcePos = _xforms.GetWorldPosition(uid, xformQuery);

            if ((sourcePos - cameraPos).Length() > microphone.Range)
                continue;

            var ev = new SurveillanceCameraMidiSendEvent(uid, args.MidiEvents);
            foreach (var monitor in camera.ActiveMonitors)
            {
                RaiseLocalEvent(monitor, ev);
            }
        }
    }

    private void OnEmoteMessage(SurveillanceCameraEmoteMessageEvent args)
    {
        if (string.IsNullOrWhiteSpace(args.Message))
            return;

        var uid = args.Source;
        if (HasComp<SurveillanceCameraSpeakerComponent>(uid) || HasComp<SurveillanceCameraMonitorComponent>(uid))
            return;

        if (!TryComp(uid, out TransformComponent? sourceXform))
            return;

        var xformQuery = GetEntityQuery<TransformComponent>();

        foreach (var (microphone, camera, xform) in EntityQuery<SurveillanceCameraMicrophoneComponent, SurveillanceCameraComponent, TransformComponent>())
        {
            if (camera.ActiveMonitors.Count == 0)
                continue;

            if (xform.MapID != sourceXform.MapID)
                continue;

            var cameraPos = _xforms.GetWorldPosition(xform, xformQuery);
            var sourcePos = _xforms.GetWorldPosition(uid, xformQuery);

            if ((sourcePos - cameraPos).Length() > microphone.Range)
                continue;

            var ev = new SurveillanceCameraSpeechSendEvent(uid, args.Message, InGameICChatType.Emote);
            foreach (var monitor in camera.ActiveMonitors)
            {
                RaiseLocalEvent(monitor, ev);
            }
        }
    }

    private void OnExpandRecipients(ExpandICChatRecipientsEvent ev)
    {
        var xformQuery = GetEntityQuery<TransformComponent>();
        var sourceXform = Transform(ev.Source);
        var sourcePos = _xforms.GetWorldPosition(sourceXform, xformQuery);

        // This function ensures that chat popups appear on camera views that have connected microphones.
        foreach (var (_, __, camera, xform) in EntityQuery<SurveillanceCameraMicrophoneComponent, ActiveListenerComponent, SurveillanceCameraComponent, TransformComponent>())
        {
            if (camera.ActiveViewers.Count == 0)
                continue;

            // get range to camera. This way wispers will still appear as obfuscated if they are too far from the camera's microphone
            var range = (xform.MapID != sourceXform.MapID)
                ? -1
                : (sourcePos - _xforms.GetWorldPosition(xform, xformQuery)).Length();

            if (range < 0 || range > ev.VoiceRange)
                continue;

            foreach (var viewer in camera.ActiveViewers)
            {
                // if the player has not already received the chat message, send it to them but don't log it to the chat
                // window. This is simply so that it appears in camera.
                if (TryComp(viewer, out ActorComponent? actor))
                    ev.Recipients.TryAdd(actor.PlayerSession, new ICChatRecipientData(range, false, true));
            }
        }
    }

    private void OnInit(EntityUid uid, SurveillanceCameraMicrophoneComponent component, ComponentInit args)
    {
        if (component.Enabled)
            EnsureComp<ActiveListenerComponent>(uid).Range = component.Range;
        else
            RemCompDeferred<ActiveListenerComponent>(uid);
    }

    public void CanListen(EntityUid uid, SurveillanceCameraMicrophoneComponent microphone, ListenAttemptEvent args)
    {
        // TODO maybe just make this a part of ActiveListenerComponent?
        if (_whitelistSystem.IsBlacklistPass(microphone.Blacklist, args.Source))
            args.Cancel();
    }

    public void RelayEntityMessage(EntityUid uid, SurveillanceCameraMicrophoneComponent component, ListenEvent args)
    {
        if (!TryComp(uid, out SurveillanceCameraComponent? camera))
            return;

        if (HasComp<SurveillanceCameraSpeakerComponent>(args.Source) || HasComp<SurveillanceCameraMonitorComponent>(args.Source))
            return;

        var ev = new SurveillanceCameraSpeechSendEvent(args.Source, args.Message, InGameICChatType.Speak);

        foreach (var monitor in camera.ActiveMonitors)
        {
            RaiseLocalEvent(monitor, ev);
        }
    }

    public void SetEnabled(EntityUid uid, bool value, SurveillanceCameraMicrophoneComponent? microphone = null)
    {
        if (!Resolve(uid, ref microphone))
            return;

        if (value == microphone.Enabled)
            return;

        microphone.Enabled = value;

        if (value)
            EnsureComp<ActiveListenerComponent>(uid).Range = microphone.Range;
        else
            RemCompDeferred<ActiveListenerComponent>(uid);
    }

    // _CS End: emote and MIDI relay expansion
}

// _CS Start: surveillance camera relay event payloads
public sealed class SurveillanceCameraSpeechSendEvent : EntityEventArgs
{
    public EntityUid Speaker { get; }
    public string Message { get; }
    public InGameICChatType Type { get; }

    public SurveillanceCameraSpeechSendEvent(EntityUid speaker, string message, InGameICChatType type)
    {
        Speaker = speaker;
        Message = message;
        Type = type;
    }
}

public sealed class SurveillanceCameraMidiSendEvent : EntityEventArgs
{
    public EntityUid Source { get; }
    public RobustMidiEvent[] MidiEvents { get; }

    public SurveillanceCameraMidiSendEvent(EntityUid source, RobustMidiEvent[] midiEvents)
    {
        Source = source;
        MidiEvents = midiEvents;
    }
}

public sealed class SurveillanceCameraMidiChannelFilterSyncEvent : EntityEventArgs
{
    public EntityUid Source { get; }
    public int Channel { get; }
    public bool Filtered { get; }

    public SurveillanceCameraMidiChannelFilterSyncEvent(EntityUid source, int channel, bool filtered)
    {
        Source = source;
        Channel = channel;
        Filtered = filtered;
    }
}
// _CS End: surveillance camera relay event payloads

