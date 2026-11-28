using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class UnsupportedNativeOverlapTests : HostFixture
{
    private sealed class World : ISceneWorld
    {
        public string? ResolveActor(string quest, int alias) => null;
        public SpeechResult Speak(string context, string topic, string? actor) => new(SpeechStatus.Spoken, 1, "");
        public void StopSpeech(string context, string actor) { }
        public PackageResult BeginPackage(string context, string package, string? actor) => new(false, "x");
        public void EndPackage(string package, string? actor) { }
        public bool SetStage(string quest, int stage) => true;
    }

    private static readonly string[] Keys =
    {
        SceneNativeAdapters.SystemKey, AliasNativeAdapters.SystemKey, ActorPackageNativeAdapters.SystemKey, CoreNativeAdapters.SystemKey,
        CustomEventNativeAdapters.SystemKey, EventRegistrationNativeAdapters.SystemKey, ActorValueNativeAdapters.SystemKey,
        KeywordNativeAdapters.SystemKey, ReferencePositionNativeAdapters.SystemKey, UnsupportedNativeAdapters.SystemKey,
    };

    private (IScriptHost Host, List<AdapterReport> Real, EvidenceGate Gate) BindRealAdapters(HostKind kind)
    {
        var gate = TestHosts.Gate(Keys.Select(key => (key, "ENGINE_POLICY")).ToArray());
        var host = Track(TestHosts.Create(kind, gate));
        var world = new World();
        var real = new List<AdapterReport>
        {
            new SceneNativeAdapters(host, gate, new SceneRuntime(_ => null, world), world, _ => { }).Bind(),
            new AliasNativeAdapters(host, gate, new QuestAliasResolver(_ => Array.Empty<AliasFillDef>(), _ => null), _ => null).Bind(),
            new CoreNativeAdapters(host, gate, () => 0.5, () => null, () => null, _ => { }).Bind(),
            new CustomEventNativeAdapters(host, gate, new CustomEventRegistry()).Bind(),
            new EventRegistrationNativeAdapters(host, gate, new EventRegistrations()).Bind(),
            new ActorValueNativeAdapters(host, gate, new ActorValueOverlay()).Bind(),
            new KeywordNativeAdapters(host, gate, new KeywordOverlay(), _ => null).Bind(),
            new ReferencePositionNativeAdapters(host, gate, _ => null).Bind(),
            new ActorPackageNativeAdapters(host, gate, _ => true, _ => { }).Bind(),
        };
        return (host, real, gate);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheProductionUnsupportedTableShadowsNoRealAdapterAndLosesNoSceneOrAliasNative(HostKind kind)
    {
        var (host, real, gate) = BindRealAdapters(kind);

        var refusals = new UnsupportedNativeAdapters(host, gate, _ => { }).Bind();

        Assert.All(refusals.Natives, outcome => Assert.Equal(AdapterState.Unsupported, outcome.State));
        Assert.All(real.Take(2).SelectMany(report => report.Natives), outcome => Assert.Equal(AdapterState.Bound, outcome.State));
        Assert.Contains(real[8].Natives, outcome => outcome.Family.Native == ActorPackageNativeAdapters.EvaluateFamily.Native && outcome.State == AdapterState.Bound);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ARowForANativeARealAdapterBoundIsReportedAsAConflict(HostKind kind)
    {
        var (host, _, gate) = BindRealAdapters(kind);
        var stale = UnsupportedNativeAdapters.Table.Append(("Actor", "EvaluatePackage", "AI package system", NativeTargetKind.EngineObject)).ToList();

        var refusals = new UnsupportedNativeAdapters(host, gate, _ => { }).Bind(stale);

        Assert.Equal(new[] { "EvaluatePackage" }, refusals.Natives.Where(outcome => outcome.State == AdapterState.Conflict).Select(outcome => outcome.Family.Native.Function));
    }
}
