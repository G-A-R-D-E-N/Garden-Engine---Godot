using System;
using Godot;
using OpenCommonwealth.Lighting;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth;

public partial class PapyrusRuntime
{
    private DeferredActivations? _activations;

    public AdapterReport? LinkNatives { get; private set; }
    public AdapterReport? OpenNatives { get; private set; }
    public AdapterReport? ActivateNatives { get; private set; }
    public AdapterReport? AnimationNatives { get; private set; }
    public AdapterReport? AnimationEventNatives { get; private set; }
    public AdapterReport? TimerNatives { get; private set; }
    public AdapterReport? InventoryNatives { get; private set; }
    public AdapterReport? PlaceNatives { get; private set; }
    public ScriptTimers Timers { get; } = new();
    public ScriptTimers GameTimers { get; } = new(ScriptTimers.GameTimeHandlerName);
    private double? _lastGameHours;
    public ReferenceAnimationNativeAdapters? Animations { get; private set; }
    public ReferenceAnimations AnimationPlayback { get; } = new(new NifAnimationClipSource(), new ReferencePoseSink());
    public HazardHost Hazards { get; } = new();
    public AttackTiming AttackTiming { get; } = new();
    private AnimationEventRouter? _animationEvents;

    // Requires: built on first use because AnimationPlayback is per runtime; the Papyrus consumer is terminal and the others only observe.
    public AnimationEventRouter AnimationEvents => _animationEvents ??= BuildAnimationEvents();

    private AnimationEventRouter BuildAnimationEvents()
    {
        var router = new AnimationEventRouter(message => GD.PushWarning($"[Anim] {message}"));
        router.Add(new PapyrusAnimationConsumer(AnimationPlayback, message => GD.PrintErr($"[NpcSpawn] {message}")));
        router.Add(AttackTiming);
        router.Add(new Services.Audio.AnimationSounds());
        return router;
    }

    private void BindReferenceNatives()
    {
        _activations = new DeferredActivations(Session!.Events);
        Animations = new ReferenceAnimationNativeAdapters(Session.Host, Session.Gate, AnimationPlayback);
        LinkNatives = new ReferenceLinkNativeAdapters(Session.Host, Session.Gate, RadicleLinkedRefs.Read).Bind();
        OpenNatives = new ReferenceOpenNativeAdapters(Session.Host, Session.Gate, new DoorOpenAccess(), AnimationPlayback).Bind();
        ActivateNatives = new ReferenceActivateNativeAdapters(Session.Host, Session.Gate, _activations).Bind();
        AnimationNatives = Animations.Bind();
        AnimationEventNatives = new ReferenceAnimationEventNativeAdapters(Session.Host, Session.Gate, AnimationPlayback).Bind();
        TimerNatives = new ReferenceTimerNativeAdapters(Session.Host, Session.Gate, Timers, GameTimers).Bind();
        InventoryNatives = new InventoryNativeAdapters(Session.Host, Session.Gate, new LedgerInventoryAccess(ContainerLedger.Current, PlayerItems.Current, IsPlayerReference, RadicleContainerSeed.Stacks)).Bind();
        PlaceNatives = new PlaceAtMeNativeAdapters(Session.Host, Session.Gate, Hazards).Bind();
        PrintUnbound(LinkNatives);
        PrintUnbound(OpenNatives);
        PrintUnbound(ActivateNatives);
        PrintUnbound(AnimationNatives);
        PrintUnbound(AnimationEventNatives);
        PrintUnbound(TimerNatives);
        PrintUnbound(InventoryNatives);
        PrintUnbound(PlaceNatives);
    }

    private static void PrintUnbound(AdapterReport report)
    {
        foreach (var outcome in report.Natives)
            if (outcome.State != AdapterState.Bound) GD.Print($"[Papyrus] {outcome.Family.Native} is not bound: {outcome.Reason}");
    }

    // Requires: main thread, outside any native callback, before the host step; due OnTimer events run first, then animation waits and events, then hazard ticks, then the activations and the custom events queued so far.
    private void RunReferenceEvents(double delta)
    {
        if (Session is null || _activations is null) return;
        Timers.Deliver(Session.Host, delta);
        DeliverGameTimers();
        AnimationPlayback.Deliver(Session.Host, delta);
        Hazards.Advance(delta);
        _activations.Drain();
        CustomEvents.Drain(Session.Host);
    }

    // Requires: the game clock; the table advances by the hours the clock moved since the last step and a clock jump backwards advances nothing.
    private void DeliverGameTimers()
    {
        if (EnvironmentController.Active?.Clock is not { } clock) { _lastGameHours = null; return; }
        var now = clock.TotalHours;
        var hours = _lastGameHours is { } last ? Math.Max(0, now - last) : 0;
        _lastGameHours = now;
        GameTimers.Deliver(Session!.Host, hours);
    }

    // Invariant: runtime overlays, scene runs, forced aliases, parked scenes and queued sends are restored only from a save.
    public void ClearRuntimeOverlays()
    {
        Keywords.Clear();
        _lastGameHours = null;
        ActorValueOverlay.Clear();
        Scenes?.StopAll();
        Scenes?.ForgetReports();
        SceneHost?.ClearAll();
        SceneResumes.Clear();
        _parkedScenes.Clear();
        CustomEvents.Clear();
        _activations?.Clear();
    }

    private void ClearReferenceEvents()
    {
        Timers.Clear();
        GameTimers.Clear();
        _lastGameHours = null;
        AnimationPlayback.Clear();
        AttackTiming.Clear();
        Hazards.Clear();
        _activations?.Clear();
        CustomEvents.Clear();
        CustomEvents.ForgetAll();
        Registrations.Clear();
        ClearRuntimeOverlays();
    }
}
