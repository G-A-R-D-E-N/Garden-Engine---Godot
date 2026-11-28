using OpenCommonwealth.Services.Ui.Character;
using System;
using System.Linq;
using Godot;
using OpenCommonwealth.Lighting;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Papyrus.Natives;

namespace OpenCommonwealth;

public partial class PapyrusRuntime
{
    public AdapterReport? CoreNatives { get; private set; }
    public AdapterReport? UnsupportedNatives { get; private set; }

    public ActorPackageNativeAdapters? PackageAdapter { get; private set; }

    public static Func<string, bool>? PackageReevaluator { get; set; } = NpcPuppetSpawner.ReevaluatePackage;
    public AdapterReport? FormNatives { get; private set; }
    public AdapterReport? KeywordNatives { get; private set; }
    public KeywordOverlay Keywords { get; } = new();
    public AdapterReport? RegistrationNatives { get; private set; }
    public EventRegistrations Registrations { get; } = new();
    public AdapterReport? ActorValueNatives { get; private set; }
    public ActorValueOverlay ActorValueOverlay { get; } = new();
    public AdapterReport? CustomEventNatives { get; private set; }
    public CustomEventRegistry CustomEvents { get; } = new();

    // Invariant: no load order means no player reference.
    private static string? PlayerRefKey() => GameLoader.HasLoadOrder ? PlayerIdentity.PlayerRefKey : null;

    // Requires: runs after every real native adapter, because TryRegisterNative keeps the first handler and the unsupported table must not shadow a bound native.
    private void BindThirdRailNatives()
    {
        CoreNatives = new CoreNativeAdapters(Session!.Host, Session.Gate, Random.Shared.NextDouble,
            () => EnvironmentController.Active?.Clock is { } clock ? clock.TotalHours / 24.0 : null,
            PlayerRefKey, message => GD.Print($"[Papyrus] {message}")).Bind();
        FormNatives = new FormLookupNativeAdapters(Session.Host, Session.Gate, RadicleFormLookup.Resolve, key => GlobalVariables.Current.Get(key) ?? RadicleFormLookup.GlobalValue(key), (key, value) => GlobalVariables.Current.Set(key, value)).Bind();
        CustomEventNatives = new CustomEventNativeAdapters(Session.Host, Session.Gate, CustomEvents).Bind();
        RegistrationNatives = new EventRegistrationNativeAdapters(Session.Host, Session.Gate, Registrations).Bind();
        ActorValueNatives = new ActorValueNativeAdapters(Session.Host, Session.Gate, ActorValueOverlay, RadicleActorValueBase.Read, message => GD.Print($"[Papyrus] {message}")).Bind();
        KeywordNatives = new KeywordNativeAdapters(Session.Host, Session.Gate, Keywords, RadicleFormLookup.Keywords).Bind();
        PositionNatives = new Services.Papyrus.Natives.ReferencePositionNativeAdapters(Session.Host, Session.Gate, ReferencePosition).Bind();
        PackageAdapter = new ActorPackageNativeAdapters(Session.Host, Session.Gate, key => PackageReevaluator?.Invoke(key) ?? false, message => GD.Print($"[Papyrus] {message}"));
        var packageNatives = PackageAdapter.Bind();
        UnsupportedNatives = new UnsupportedNativeAdapters(Session.Host, Session.Gate, message => GD.PushWarning($"[Papyrus] {message}")).Bind();
        foreach (var outcome in CoreNatives.Natives.Concat(FormNatives.Natives).Concat(CustomEventNatives.Natives).Concat(RegistrationNatives.Natives).Concat(ActorValueNatives.Natives).Concat(KeywordNatives.Natives).Concat(PositionNatives.Natives).Concat(packageNatives.Natives))
            if (outcome.State != AdapterState.Bound) GD.Print($"[Papyrus] {outcome.Family.Native} is not bound: {outcome.Reason}");
        if (Session.Host is OpenCommonwealth.Services.Papyrus.Host.RadicleScriptHost radicle)
            GD.Print($"[Papyrus] {radicle.PublishUnboundRefusals()} declared natives without an engine implementation now refuse by name");
    }
}
