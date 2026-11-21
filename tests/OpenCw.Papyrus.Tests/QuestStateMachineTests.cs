using System;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestStateMachineTests : HostFixture
{
    private const string Quest = "000800:Quests.esp";
    private const string SecondQuest = "000801:Quests.esp";

    private static readonly (string Key, string Status)[] BaseRows =
    {
        (QuestStateMachine.LifecycleKey, "PROVEN"),
        (QuestStateMachine.SetActiveKey, "UNPROVEN"),
        (QuestRuntime.FragmentTimingKey, "UNPROVEN"),
        ("owner.engine_identity", "ENGINE_POLICY"),
    };

    private static readonly ScriptSpec[] Scripts =
    {
        TestHosts.Stub("Quest", null,
            "Function Start() native",
            "Function Stop() native",
            "Function Reset() native",
            "Function CompleteQuest() native",
            "Function SetActive(Bool abActive) native",
            "Function SetStage(Int aiStage) native"),
        TestHosts.Stub("QuestScript", null, "Function OnInit()\nEndFunction\n"),
    };

    private static readonly ScriptSpec[] StaticScripts =
    {
        TestHosts.Stub("Quest", null,
            "Function Start() global native",
            "Function Stop() native",
            "Function Reset() native",
            "Function CompleteQuest() native",
            "Function SetActive(Bool abActive) native",
            "Function SetStage(Int aiStage) native"),
        TestHosts.Stub("Probe", null, "Function Ping() global native"),
        TestHosts.Stub("Caller", null,
            "Function RunControl()\n  Probe.Ping()\nEndFunction\n" +
            "Function Run()\n  Quest.Start()\nEndFunction\n"),
    };

    private static readonly ScriptSpec QuestSource = TestHosts.Stub("QuestSource", null, "Quest Function Get() global native");

    private static ScriptSpec Caller(string target) => TestHosts.Stub("Caller", null,
        $"Function Run()\n  {target}\n  target.Start()\nEndFunction\n");

    private static VmResult RunCaller(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    private static EvidenceGate Gate(params (string Key, string Status)[] extra) =>
        TestHosts.Gate(BaseRows.Concat(extra).ToArray());

    public static IEnumerable<object[]> HostCases() => TestHosts.HostKinds;

    [Fact]
    public void TheLifecycleTransitionsAreProven()
    {
        var machine = new QuestStateMachine(Gate());
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));

        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.Equal(QuestState.Running, machine.StateOf(Quest));

        Assert.True(machine.Apply(Quest, QuestNative.Stop));
        Assert.Equal(QuestState.Stopped, machine.StateOf(Quest));

        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.CompleteQuest));
        Assert.Equal(QuestState.Completed, machine.StateOf(Quest));

        Assert.True(machine.Apply(Quest, QuestNative.Reset));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));
    }

    [Fact]
    public void AnIllegalTransitionIsRefused()
    {
        var machine = new QuestStateMachine(Gate());

        Assert.False(machine.Apply(Quest, QuestNative.CompleteQuest));
        Assert.False(machine.Apply(Quest, QuestNative.Stop));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));
    }



    [Fact]
    public void SetStageAppliesOnceTheTimingIsProven()
    {
        var gate = Gate((QuestRuntime.FragmentTimingKey, "PROVEN"));
        var machine = new QuestStateMachine(gate);

        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 20));
        Assert.Equal(20, machine.StageOf(Quest));
    }


    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheNativeBindingsAreDeterministic(HostKind kind)
    {
        var gate = Gate();
        var first = new QuestStateMachine(gate).Bind(Track(TestHosts.Create(kind, gate, Scripts)), new[] { Quest });
        var second = new QuestStateMachine(gate).Bind(Track(TestHosts.Create(kind, gate, Scripts)), new[] { Quest });

        Assert.Equal(first.Natives, second.Natives);
        Assert.Equal(first.Bound, second.Bound);
        Assert.Equal(first.Blocked, second.Blocked);
    }


    [Fact]
    public void ReviewQuestBindingRefusesANoneReceiver()
    {
        var gate = Gate();
        var host = Track(TestHosts.Create(HostKind.Radicle, gate, Scripts.Append(QuestSource).Append(
            TestHosts.Stub("Caller", null,
                "Function RunBound()\n  Quest target = QuestSource.Get()\n  target.Start()\nEndFunction\n" +
                "Function RunNone()\n  Quest target = None\n  target.Start()\nEndFunction\n")).ToArray()));
        host.RegisterNative(new NativeId("QuestSource", "Get"),
            _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(Quest))));
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { Quest });
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var bound = host.RunEvent(instance, "RunBound");
        Assert.True(bound.Completed, TestHosts.Describe(host, bound));
        Assert.Equal(QuestState.Running, machine.StateOf(Quest));
        var result = host.RunEvent(instance, "RunNone");

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(QuestState.Running, machine.StateOf(Quest));
        Assert.Equal(0, machine.StageOf(Quest));
    }

    [Fact]
    public void ReviewQuestBindingRefusesAnUnboundFormOwnerInApplyNative()
    {
        var gate = Gate();
        var host = Track(TestHosts.Create(HostKind.Radicle, gate, Scripts.Append(QuestSource)
            .Append(Caller("Quest target = QuestSource.Get()")).ToArray()));
        host.RegisterNative(new NativeId("QuestSource", "Get"),
            _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(SecondQuest))));
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { Quest });

        var result = RunCaller(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Contains("the native needs a resolved, known Quest form owner", TestHosts.Describe(host, result),
            StringComparison.Ordinal);
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(SecondQuest));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ReviewQuestRebindingCannotOverwriteAnotherQuest(HostKind kind)
    {
        var gate = Gate();
        var host = Track(TestHosts.Create(kind, gate, Scripts.Append(QuestSource).Append(Caller("Quest target = QuestSource.Get()")).ToArray()));
        host.RegisterNative(new NativeId("QuestSource", "Get"),
            _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(Quest))));
        var first = new QuestStateMachine(gate);
        var second = new QuestStateMachine(gate);
        first.Bind(host, new[] { Quest });
        var report = second.Bind(host, new[] { SecondQuest });
        var result = RunCaller(host);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(QuestState.Running, first.StateOf(Quest));
        Assert.Equal(QuestState.NotStarted, second.StateOf(SecondQuest));
        Assert.Equal(0, report.Bound);
    }

    [Fact]
    public void ReviewQuestBindingRefusesAmbiguousStaticOwnership()
    {
        var gate = Gate();
        var host = Track(TestHosts.Create(HostKind.Radicle, gate, StaticScripts));
        host.RegisterNative(new NativeId("Probe", "Ping"), _ => PapyrusValue.None, NativeTargetKind.Static);
        var machine = new QuestStateMachine(gate);
        var report = machine.Bind(host, new[] { Quest, SecondQuest });
        Assert.Equal(6, report.Bound);
        Assert.Equal(0, report.Blocked);
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        Assert.True(host.RunEvent(instance, "RunControl").Completed, "a Static native called statically must succeed");
        var result = host.RunEvent(instance, "Run");

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));
    }

}
