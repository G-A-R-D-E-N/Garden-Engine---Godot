using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class SceneNativeAdaptersTests : HostFixture
{
    private const string Scene = "03467E:Test.esm";
    private const string Quest = "027411:Test.esm";

    private sealed class World : ISceneWorld
    {
        public List<string> Spoken { get; } = new();

        public string? ResolveActor(string quest, int alias) => "A1:Test.esm";

        public SpeechResult Speak(string context, string topic, string? actor)
        {
            Spoken.Add($"{topic}|{actor}");
            return new SpeechResult(SpeechStatus.Spoken, 1, "");
        }

        public void StopSpeech(string context, string actor) { }

        public PackageResult BeginPackage(string context, string package, string? actor) => new(false, "override absent");

        public void EndPackage(string package, string? actor) { }

        public bool SetStage(string quest, int stage) => true;
    }

    private static SceneDef Def(string? refusal = null) => new(Scene, Quest, new[] { new ScenePhaseDef(0, "", null, null) },
        new[] { new SceneActionDef("Dialogue#1", SceneActionKind.Dialogue, 1, 0, 0, false, "T0:Test.esm", 0) }, null, 10, refusal);

    private (IScriptHost Host, SceneRuntime Runtime, World World, List<string> Logged) Setup(HostKind kind, string? refusal = null)
    {
        var gate = TestHosts.Gate((SceneNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Scene", null, "Function Start() native", "Function ForceStart() native", "Function Stop() native", "bool Function IsPlaying() native"),
            TestHosts.Stub("ObjectReference", null, "Function Say(ObjectReference akTopic, ObjectReference akSpeakAs = None, bool abInHead = false) native"),
            TestHosts.Stub("Probe", null, "Scene Function MakeScene() global native", "ObjectReference Function MakeRef() global native", "ObjectReference Function MakeTopic() global native"),
            new ScriptSpec("Caller",
                "Scriptname Caller\nBool Function Twice()\n  Scene s = Probe.MakeScene()\n  s.Start()\n  s.Start()\n  Return s.IsPlaying()\nEndFunction\n" +
                "Bool Function StartThenStop()\n  Scene s = Probe.MakeScene()\n  s.Start()\n  s.Stop()\n  Return s.IsPlaying()\nEndFunction\n" +
                "Function SayIt()\n  ObjectReference r = Probe.MakeRef()\n  r.Say(Probe.MakeTopic())\nEndFunction\n")));
        host.RegisterNative(new NativeId("Probe", "MakeScene"), _ => PapyrusValue.FromObject(new EngineObjectReference(Scene)));
        host.RegisterNative(new NativeId("Probe", "MakeRef"), _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForPlacedReference("000800:Test.esm", "A1:Test.esm"))));
        host.RegisterNative(new NativeId("Probe", "MakeTopic"), _ => PapyrusValue.FromObject(new EngineObjectReference("T0:Test.esm")));
        var world = new World();
        var runtime = new SceneRuntime(key => key == Scene ? Def(refusal) : null, world);
        var logged = new List<string>();
        var report = new SceneNativeAdapters(host, gate, runtime, world, logged.Add).Bind();
        Assert.Equal(6, report.Bound);
        return (host, runtime, world, logged);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void StartQueuesOneRunSoASecondStartIsANoOpAndStopCancelsIt(HostKind kind)
    {
        var (host, runtime, world, _) = Setup(kind);
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var twice = host.RunEvent(instance, "Twice");
        Assert.True(twice.Completed, TestHosts.Describe(host, twice));
        Assert.True(twice.Value.Flag);
        Assert.Empty(world.Spoken);
        runtime.Tick(0);
        Assert.Single(world.Spoken);

        runtime.Stop(Scene);
        var stopped = host.RunEvent(instance, "StartThenStop");
        Assert.True(stopped.Completed, TestHosts.Describe(host, stopped));
        Assert.False(stopped.Value.Flag);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ARefusedSceneStopsTheScriptAndNamesTheSceneAndAction(HostKind kind)
    {
        var (host, runtime, _, logged) = Setup(kind, "action Radio#2 is a Radio action, which Garden does not run");
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, "Twice");

        Assert.False(result.Completed);
        Assert.False(runtime.IsPlaying(Scene));
        Assert.Contains(logged, line => line.Contains(Scene) && line.Contains("Radio#2"));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void SaySpeaksTheTopicAsTheReceiver(HostKind kind)
    {
        var (host, _, world, _) = Setup(kind);
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, "SayIt");

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { "T0:Test.esm|A1:Test.esm" }, world.Spoken);
    }
}
