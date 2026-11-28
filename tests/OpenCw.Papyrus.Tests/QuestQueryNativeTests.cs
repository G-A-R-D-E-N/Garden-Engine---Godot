using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestQueryNativeTests : HostFixture
{
    private const string Quest = "000800:Quests.esp";

    private static readonly ScriptSpec QuestStub = TestHosts.Stub("Quest", null,
        "Bool Function SetCurrentStageID(Int aiStageID) native",
        "Function Start() native",
        "Bool Function IsStageDone(Int auiStageID) native",
        "Int Function GetCurrentStageID() native",
        "Bool Function IsRunning() native",
        "Bool Function IsStopped() native",
        "Bool Function IsCompleted() native");

    private static readonly ScriptSpec SourceStub = TestHosts.Stub("QuestSource", null, "Quest Function Get() global native", "Quest Function GetBare() global native");

    private static readonly ScriptSpec Reader = new("Reader",
        "Scriptname Reader\n" +
        "Bool Property Done5 Auto\n" +
        "Bool Property Done6 Auto\n" +
        "Int Property Stage Auto\n" +
        "Int Property Current Auto\n" +
        "Bool Property Running Auto\n" +
        "Bool Property Stopped Auto\n" +
        "Function Read()\n" +
        "  Quest target = QuestSource.Get()\n" +
        "  Done5 = target.IsStageDone(5)\n" +
        "  Done6 = target.IsStageDone(6)\n" +
        "  Stage = target.GetCurrentStageID()\n" +
        "  Current = target.GetCurrentStageID()\n" +
        "  Running = target.IsRunning()\n" +
        "  Stopped = target.IsStopped()\n" +
        "EndFunction\n" +
        "Function Bump()\n" +
        "  Quest target = QuestSource.GetBare()\n" +
        "  target.SetCurrentStageID(6)\n" +
        "EndFunction\n");

    private (RadicleScriptHost Host, QuestStateMachine Machine, ScriptInstance Reader) Setup()
    {
        var gate = TestHosts.Gate(new[] { QuestStateMachine.LifecycleKey, "owner.engine_identity", QuestRuntime.FragmentTimingKey }
            .Select(key => (key, "ENGINE_POLICY")).ToArray());
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, gate, QuestStub, SourceStub, Reader));
        host.RegisterNative(new NativeId("QuestSource", "Get"),
            _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(Quest))));
        host.RegisterNative(new NativeId("QuestSource", "GetBare"), _ => PapyrusValue.FromObject(new EngineObjectReference(Quest)));
        var machine = new QuestStateMachine(gate);
        machine.Bind(host, new[] { Quest });
        var reader = host.Attach("Reader");
        Assert.True(host.Initialize(reader).Completed, reader.Failure);
        return (host, machine, reader);
    }

    private static bool Flag(RadicleScriptHost host, ScriptInstance reader, string name) =>
        host.TryReadScalarProperty(reader, name, out var value) && value is true;

    [Fact]
    public void AScriptReadsTheStageAndStateTheMachineHolds()
    {
        var (host, machine, reader) = Setup();
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 5));

        var run = host.RunEvent(reader, "Read");

        Assert.True(run.Completed, run.Message + " / " + host.LastNativeFailure);
        Assert.True(Flag(host, reader, "Done5"));
        Assert.False(Flag(host, reader, "Done6"));
        Assert.True(host.TryReadScalarProperty(reader, "Stage", out var stage));
        Assert.Equal(5, Convert.ToInt32(stage));
        Assert.True(host.TryReadScalarProperty(reader, "Current", out var current));
        Assert.Equal(5, Convert.ToInt32(current));
        Assert.True(Flag(host, reader, "Running"));
        Assert.False(Flag(host, reader, "Stopped"));
    }

    [Fact]
    public void AQuestThatWasNeverStartedReadsAsStageZeroNotRunning()
    {
        var (host, _, reader) = Setup();

        var run = host.RunEvent(reader, "Read");

        Assert.True(run.Completed, run.Message + " / " + host.LastNativeFailure);
        Assert.False(Flag(host, reader, "Done5"));
        Assert.False(Flag(host, reader, "Running"));
        Assert.True(Flag(host, reader, "Stopped"));
        Assert.True(host.TryReadScalarProperty(reader, "Stage", out var stage));
        Assert.Equal(0, Convert.ToInt32(stage));
    }

    [Fact]
    public void AQuestHeldAsAPropertyByItsFormKeyAloneCanBeStaged()
    {
        var (host, machine, reader) = Setup();
        Assert.True(machine.Apply(Quest, QuestNative.Start));

        var run = host.RunEvent(reader, "Bump");

        Assert.True(run.Completed, run.Message + " / " + host.LastNativeFailure);
        Assert.True(machine.StageDone(Quest, 6));
    }
}
