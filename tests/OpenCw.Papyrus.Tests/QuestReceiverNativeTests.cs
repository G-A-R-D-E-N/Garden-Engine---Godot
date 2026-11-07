using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestReceiverNativeTests
{
    private const string First = "000800:Quests.esp";
    private const string Second = "000801:Quests.esp";

    private static EvidenceGate Gate(string? blocked = null, bool stageAllowed = false)
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.Parameters,
            EvidenceKeys.OnInitBarrier, EvidenceKeys.NativeTarget, EvidenceKeys.Dispatch,
            QuestStateMachine.LifecycleKey, "owner.engine_identity", QuestStageDispatch.TimingKey };
        var rows = keys.Select(key => new { key, status = key == blocked
            || key == QuestStageDispatch.TimingKey && !stageAllowed ? "UNPROVEN" : "ENGINE_POLICY" });
        return new EvidenceGate(EvidenceRegistry.Load(System.Text.Json.JsonSerializer.Serialize(
            new { schema = EvidenceRegistry.SchemaName, rows })));
    }

    private static PapyrusValue Receiver(string quest) =>
        PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(quest)));

    private static ScriptHost Host(EvidenceGate gate, params PapyrusFunction[] functions)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("Caller", functions, Array.Empty<string>()));
        return new ScriptHost(library, gate);
    }

    private static PapyrusFunction Call(string name, string quest, string native) => new(name, new[]
    {
        PapyrusInstruction.CallMethod(PapyrusOperand.Constant(Receiver(quest)), native,
            PapyrusOperand.Local(0), Array.Empty<PapyrusOperand>(), receiverType: "Quest"),
        PapyrusInstruction.Return(PapyrusOperand.Local(0)),
    });

    [Fact]
    public void TwoKnownQuestsRouteThroughIndependentBytecodeReceivers()
    {
        var gate = Gate();
        var host = Host(gate, Call("First", First, "Start"), Call("Second", Second, "Start"),
            Call("StopFirst", First, "Stop"));
        var machine = new QuestStateMachine(gate);
        var report = machine.Bind(host, new[] { First, Second });
        Assert.Equal(4, report.Bound);
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        Assert.True(host.RunEvent(instance, "First").Completed);
        Assert.Equal(QuestState.Running, machine.StateOf(First));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Second));
        Assert.True(host.RunEvent(instance, "Second").Completed);
        Assert.True(host.RunEvent(instance, "StopFirst").Completed);
        Assert.Equal(QuestState.Stopped, machine.StateOf(First));
        Assert.Equal(QuestState.Running, machine.StateOf(Second));
        Assert.Equal(3, machine.Applied);
    }

    [Theory]
    [InlineData(QuestNative.Start)]
    [InlineData(QuestNative.Reset)]
    public void DirectLifecycleCallsCannotBypassTheirGate(QuestNative native)
    {
        var machine = new QuestStateMachine(Gate(QuestStateMachine.LifecycleKey));
        Assert.False(machine.Apply(First, native));
        Assert.Empty(machine.States);
        Assert.Equal(0, machine.Applied);
        Assert.Equal(1, machine.Refused);
    }

    [Fact]
    public void ReceiverBindingsRequireTheOwnerIdentityGate()
    {
        var gate = Gate("owner.engine_identity");
        var machine = new QuestStateMachine(gate);
        var report = machine.Bind(Host(gate), new[] { First });
        Assert.Equal(0, report.Bound);
        Assert.Contains(report.Natives, row => row.Key == "owner.engine_identity");
        Assert.Equal(0, machine.Applied);
    }

    [Theory]
    [InlineData(QuestNative.Start)]
    [InlineData(QuestNative.Stop)]
    [InlineData(QuestNative.Reset)]
    [InlineData(QuestNative.CompleteQuest)]
    public void ExtraArgumentsCannotMutateQuestState(QuestNative native)
    {
        var gate = Gate();
        var host = Host(gate);
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { First });
        machine.Apply(First, QuestNative.Start);
        Assert.Throws<InvalidOperationException>(() => Invoke(host, native, Receiver(First), PapyrusValue.FromInt(7)));
        Assert.Equal(QuestState.Running, machine.StateOf(First));
        Assert.Equal(1, machine.Applied);
    }

    [Theory]
    [InlineData(QuestNative.Stop)]
    [InlineData(QuestNative.Reset)]
    [InlineData(QuestNative.CompleteQuest)]
    public void VoidLifecycleNativesReturnNone(QuestNative native)
    {
        var gate = Gate();
        var host = Host(gate);
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { First });
        machine.Apply(First, QuestNative.Start);
        Assert.Equal(PapyrusValueKind.None, Invoke(host, native, Receiver(First)).Kind);
        Assert.Equal(2, machine.Applied);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void UnknownAndUnsupportedReceiversCannotMutateQuestState(int kind)
    {
        var target = kind switch
        {
            0 => Receiver(Second),
            1 => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForPlacedReference(First, "placed"))),
            2 => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForDynamicReference(First, "dynamic"))),
            3 => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForQuestAlias(First, 1))),
            4 => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForRuntimeInstance(First, 1))),
            5 => PapyrusValue.FromObject(new EngineObjectReference(First)),
            7 => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(First))
                { Identity = "forged" }),
            _ => PapyrusValue.None,
        };
        var gate = Gate();
        var host = Host(gate);
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { First });
        Assert.Throws<InvalidOperationException>(() => Invoke(host, QuestNative.Start, target));
        Assert.Empty(machine.States);
        Assert.Equal(0, machine.Applied);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MalformedStageArgumentsCannotMutateQuestState(int shape)
    {
        var arguments = shape switch
        {
            0 => Array.Empty<PapyrusValue>(),
            1 => new[] { PapyrusValue.FromBool(true) },
            2 => new[] { PapyrusValue.FromInt(20), PapyrusValue.FromInt(30) },
            _ => new[] { PapyrusValue.FromInt((long)int.MaxValue + 1) },
        };
        var gate = Gate(stageAllowed: true);
        var host = Host(gate);
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { First });
        Assert.Throws<InvalidOperationException>(() => Invoke(host, QuestNative.SetStage, Receiver(First), arguments));
        Assert.Equal(0, machine.StageOf(First));
        Assert.Equal(0, machine.Applied);
        Assert.Equal(1, machine.Refused);
    }

    [Fact]
    public void AnAllowedStageRoutesToOnlyItsReceiver()
    {
        var gate = Gate(stageAllowed: true);
        var host = Host(gate);
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { First, Second });
        var result = Invoke(host, QuestNative.SetStage, Receiver(Second), PapyrusValue.FromInt(20));
        Assert.True(result.Flag);
        Assert.Equal(0, machine.StageOf(First));
        Assert.Equal(20, machine.StageOf(Second));
        Assert.Equal(1, machine.Applied);
    }

    private static PapyrusValue Invoke(ScriptHost host, QuestNative native, PapyrusValue target,
        params PapyrusValue[] arguments) => ((INativeTarget)host).Invoke(new NativeId("Quest", native.ToString()),
            NativeTargetKind.EngineObject, target, arguments);
}
