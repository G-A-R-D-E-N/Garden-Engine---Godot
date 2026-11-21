using System.Text;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class LatentSaveTests : HostFixture
{
    private static readonly NativeId Answer = new("External", "Answer");

    private static readonly ScriptSpec ExternalSpec = new("External",
        "Scriptname External\n" +
        "Int Function Answer() global native\n");

    private static readonly ScriptSpec CallerSpec = new("Caller",
        "Scriptname Caller\n" +
        "Function Run()\n" +
        "  Int got = External.Answer()\n" +
        "EndFunction\n");

    private (RadicleScriptHost Host, GameSession Session) Waiting()
    {
        var gate = TestHosts.Gate();
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, gate, ExternalSpec, CallerSpec));
        host.RegisterNative(Answer, _ => throw new NativeWait(5), NativeTargetKind.Static);
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
        return (host, new GameSession(host, gate));
    }

    // Invariant: a save made while a script call waits on a latent native must be refused, because the wait cannot be written to the save.
    [Fact]
    public void ASaveIsRefusedWhileAScriptCallWaitsOnALatentNativeAndAllowedAfterItCompletes()
    {
        var (host, session) = Waiting();
        Assert.Equal(1, host.PendingLatentWaits);
        var refusal = Assert.Throws<InvalidOperationException>(() => GameScriptSave.Capture(session, null));
        Assert.Contains("latent", refusal.Message);
        Assert.True(host.CompleteLatent(5, PapyrusValue.FromInt(1)));
        host.RunStep(32);
        Assert.Equal(0, host.PendingLatentWaits);
        GameScriptSave.Capture(session, null);
    }

    // Invariant: a committed load must abandon the waits of the world it replaced, so a late completion is refused and the abandon event fires once per wait.
    [Fact]
    public void ACommittedLoadAbandonsTheWaitingCallsAndRefusesTheirLateCompletion()
    {
        var (host, session) = Waiting();
        var abandoned = new List<long>();
        host.LatentAbandoned += abandoned.Add;
        var outcome = GameScriptSave.Restore(new Dictionary<string, byte[]>(), session);
        Assert.True(outcome.Ok);
        Assert.Equal(new long[] { 5 }, abandoned);
        Assert.Equal(0, host.PendingLatentWaits);
        Assert.False(host.CompleteLatent(5, PapyrusValue.FromInt(1)));
        Assert.Equal(1, host.RefusedCompletions);
        GameScriptSave.Capture(session, null);
    }

    // Invariant: a load refused before anything is applied must leave the waiting calls waiting.
    [Fact]
    public void ARefusedLoadKeepsTheWaitingCalls()
    {
        var (host, session) = Waiting();
        var abandoned = new List<long>();
        host.LatentAbandoned += abandoned.Add;
        var sections = new Dictionary<string, byte[]> { [QuestStateSave.SectionName] = Encoding.UTF8.GetBytes("not json") };
        Assert.False(GameScriptSave.Restore(sections, session).Ok);
        Assert.Empty(abandoned);
        Assert.Equal(1, host.PendingLatentWaits);
        Assert.True(host.CompleteLatent(5, PapyrusValue.FromInt(1)));
    }
}
