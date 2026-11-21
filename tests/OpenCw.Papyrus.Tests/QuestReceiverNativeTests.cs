using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestReceiverNativeTests : HostFixture
{
    private const string First = "000800:Quests.esp";
    private const string Second = "000801:Quests.esp";

    private static readonly string[] QuestNatives =
    {
        "Function Start() native",
        "Function Stop() native",
        "Function Reset() native",
        "Function CompleteQuest() native",
        "Function SetActive(Bool abActive) native",
        "Bool Function SetStage(Int aiStage) native",
    };

    private static readonly ScriptSpec SourceStub = TestHosts.Stub("QuestSource", null,
        "Quest Function First() global native",
        "Quest Function Second() global native",
        "Quest Function Probe() global native");

    private static ScriptSpec QuestStub(string? native = null, string? declaration = null) => TestHosts.Stub("Quest", null,
        QuestNatives.Select(line => native is not null && line.Contains($" {native}(", StringComparison.Ordinal)
            ? declaration! : line).ToArray());

    private static EvidenceGate Gate(string? blocked = null, bool stageAllowed = false) => TestHosts.Gate(
        new[] { QuestStateMachine.LifecycleKey, "owner.engine_identity", QuestRuntime.FragmentTimingKey }
            .Select(key => (key, key == blocked || key == QuestRuntime.FragmentTimingKey && !stageAllowed
                ? "UNPROVEN" : "ENGINE_POLICY"))
            .ToArray());

    private static PapyrusValue Receiver(string quest) =>
        PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(quest)));

    private static ScriptSpec Caller(params string[] functions) => new("Caller",
        "Scriptname Caller\n" + string.Concat(functions));

    private static string Call(string name, string quest, string statement) =>
        $"Function {name}()\n  Quest target = QuestSource.{quest}()\n  target.{statement}\nEndFunction\n";

    private IScriptHost QuestHost(HostKind kind, EvidenceGate gate, PapyrusValue? probe, params string[] functions) =>
        QuestHost(kind, gate, probe, QuestStub(), functions);

    private IScriptHost QuestHost(HostKind kind, EvidenceGate gate, PapyrusValue? probe, ScriptSpec quest,
        params string[] functions)
    {
        var host = Track(TestHosts.Create(kind, gate, quest, SourceStub, Caller(functions)));
        host.RegisterNative(new NativeId("QuestSource", "First"), _ => Receiver(First));
        host.RegisterNative(new NativeId("QuestSource", "Second"), _ => Receiver(Second));
        host.RegisterNative(new NativeId("QuestSource", "Probe"), _ => probe ?? PapyrusValue.None);
        return host;
    }

    private static VmResult RunCaller(IScriptHost host, string function)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, function);
    }

    private static PapyrusValue UnsupportedReceiver(int kind) => kind switch
    {
        0 => Receiver(Second),
        1 => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForPlacedReference(First, "placed"))),
        2 => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForDynamicReference(First, "dynamic"))),
        3 => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForQuestAlias(First, 1))),
        4 => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForRuntimeInstance(First, 1))),
        5 => PapyrusValue.FromObject(new EngineObjectReference(First)),
        7 => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(First)) { Identity = "forged" }),
        _ => PapyrusValue.None,
    };

    private static void AssertStopped(IScriptHost host, VmResult result)
    {
        Assert.False(result.Completed, TestHosts.Describe(host, result));
    }

    public static IEnumerable<object[]> VoidLifecycleCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            yield return new object[] { host[0], "Halt", "Stop()" };
            yield return new object[] { host[0], "Restart", "Reset()" };
            yield return new object[] { host[0], "Finish", "CompleteQuest()" };
        }
    }

    public static IEnumerable<object[]> UnsupportedReceiverCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            for (var receiver = 0; receiver <= 7; receiver++)
            {
                yield return new object[] { host[0], receiver };
            }
        }
    }

    public static IEnumerable<object[]> ExtraArgumentCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            foreach (var native in new[] { QuestNative.Start, QuestNative.Stop, QuestNative.Reset, QuestNative.CompleteQuest })
            {
                yield return new object[] { host[0], native };
            }
        }
    }

    public static IEnumerable<object[]> MalformedStageCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            for (var shape = 0; shape <= 2; shape++)
            {
                yield return new object[] { host[0], shape };
            }
        }
    }




    [Theory]
    [MemberData(nameof(ExtraArgumentCases))]
    public void ExtraArgumentsCannotMutateQuestState(HostKind kind, QuestNative native)
    {
        var gate = Gate();
        var host = QuestHost(kind, gate, null, QuestStub(native.ToString(), $"Function {native}(Int aiExtra) native"),
            Call("Run", "First", $"{native}(7)"));
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { First });
        machine.Apply(First, QuestNative.Start);

        var result = RunCaller(host, "Run");

        AssertStopped(host, result);
        Assert.Contains("needs no arguments", TestHosts.Describe(host, result), StringComparison.Ordinal);
        Assert.Equal(QuestState.Running, machine.StateOf(First));
    }

    [Theory]
    [MemberData(nameof(VoidLifecycleCases))]
    public void VoidLifecycleNativesReturnNone(HostKind kind, string function, string statement)
    {
        var gate = Gate();
        var host = QuestHost(kind, gate, null, Call("Begin", "First", "Start()"), Call(function, "First", statement));
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { First });
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        Assert.True(host.RunEvent(instance, "Begin").Completed);

        var result = host.RunEvent(instance, function);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(PapyrusValueKind.None, result.Value.Kind);
    }

    [Theory]
    [MemberData(nameof(UnsupportedReceiverCases))]
    public void UnknownAndUnsupportedReceiversCannotMutateQuestState(HostKind kind, int receiverKind)
    {
        var gate = Gate();
        var host = QuestHost(kind, gate, UnsupportedReceiver(receiverKind), Call("Run", "Probe", "Start()"));
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { First });
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        AssertStopped(host, host.RunEvent(instance, "Run"));

        Assert.Equal(QuestState.NotStarted, machine.StateOf(First));
    }

    [Theory]
    [MemberData(nameof(MalformedStageCases))]
    public void MalformedStageArgumentsCannotMutateQuestState(HostKind kind, int shape)
    {
        var (declaration, call) = shape switch
        {
            0 => ("Bool Function SetStage() native", "SetStage()"),
            1 => ("Bool Function SetStage(Bool abStage) native", "SetStage(True)"),
            _ => ("Bool Function SetStage(Int aiStage, Int aiExtra) native", "SetStage(20, 30)"),
        };
        var gate = Gate(stageAllowed: true);
        var host = QuestHost(kind, gate, null, QuestStub("SetStage", declaration), Call("Run", "First", call));
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { First });

        var result = RunCaller(host, "Run");

        AssertStopped(host, result);
        Assert.Equal(0, machine.StageOf(First));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnAllowedStageRoutesToOnlyItsReceiver(HostKind kind)
    {
        var gate = Gate(stageAllowed: true);
        var host = QuestHost(kind, gate, null,
            "Bool Function Advance()\n  Quest target = QuestSource.Second()\n  Return target.SetStage(20)\nEndFunction\n");
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { First, Second });
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, "Advance");

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.True(result.Value.Flag);
        Assert.Equal(0, machine.StageOf(First));
        Assert.Equal(20, machine.StageOf(Second));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TwoKnownQuestsRouteThroughIndependentBytecodeReceivers(HostKind kind)
    {
        var gate = Gate();
        var host = QuestHost(kind, gate, null,
            Call("First", "First", "Start()"),
            Call("Second", "Second", "Start()"),
            Call("StopFirst", "First", "Stop()"));
        var machine = new QuestStateMachine(gate);
        var report = machine.Bind(host, new[] { First, Second });
        Assert.Equal(6, report.Bound);
        Assert.Equal(0, report.Blocked);
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        Assert.True(host.RunEvent(instance, "First").Completed);
        Assert.Equal(QuestState.Running, machine.StateOf(First));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Second));
        Assert.True(host.RunEvent(instance, "Second").Completed);
        Assert.True(host.RunEvent(instance, "StopFirst").Completed);
        Assert.Equal(QuestState.Stopped, machine.StateOf(First));
        Assert.Equal(QuestState.Running, machine.StateOf(Second));
    }
}
