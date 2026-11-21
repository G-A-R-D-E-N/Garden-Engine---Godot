using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class RecordFragmentTests : HostFixture
{
    private const string Info = "Info:Probe.esp:012345";
    private const string Term = "Term:Probe.esp:ABCDEF";

    private GameSession Session(out List<int> hits)
    {
        var gate = TestHosts.Gate();
        var host = Track(TestHosts.Create(HostKind.Radicle, gate,
            TestHosts.Stub("ObjectReference"),
            TestHosts.Stub("Recorder", null, "Function Hit(Int n) global native"),
            TestHosts.Stub("TIF__00012345", null,
                "Function Fragment_Begin(ObjectReference akSpeakerRef)\n  Recorder.Hit(1)\nEndFunction",
                "Function Fragment_End(ObjectReference akSpeakerRef)\n  Recorder.Hit(2)\nEndFunction"),
            TestHosts.Stub("TERM_Probe_00ABCDEF", null,
                "Function Fragment_Terminal_03(ObjectReference akTerminalRef)\n  Recorder.Hit(30)\nEndFunction")));
        var seen = new List<int>();
        host.RegisterNative(new NativeId("Recorder", "Hit"), args =>
        {
            seen.Add((int)args[0].Integer);
            return PapyrusValue.None;
        });
        hits = seen;
        return new GameSession(host, gate)
        {
            PexScripts = new[] { "TIF__00012345", "TERM_Probe_00ABCDEF" }.ToDictionary(name => name, _ => Array.Empty<byte>()),
        };
    }

    // Detects: a dialogue or terminal fragment that is queued but never run, or one that runs before the drain; the begin and end calls must reach the info script in order.
    [Fact]
    public void QueuedInfoAndTerminalFragmentsRunOnTheNextDrain()
    {
        var session = Session(out var hits);
        session.QueueRecordFragment(RecordFragmentScripts.InfoPrefix, Info, 0x00012345, RecordFragmentScripts.InfoBegin, "Ref:Probe.esp");
        session.QueueRecordFragment(RecordFragmentScripts.InfoPrefix, Info, 0x00012345, RecordFragmentScripts.InfoEnd);
        session.QueueRecordFragment(RecordFragmentScripts.TerminalPrefix, Term, 0x00ABCDEF, RecordFragmentScripts.TerminalFunction(3), "Ref:Probe.esp");
        Assert.Empty(hits);
        session.RunQueuedFragments();
        Assert.Equal(new[] { 1, 2, 30 }, hits);
        Assert.Equal(3, session.RecordFragmentsRun);
        Assert.Equal(0, session.RecordFragmentsMissing);
    }

    // Detects: a record with no fragment script being counted as a run or throwing; it must be counted missing and the queue must keep draining.
    [Fact]
    public void ARecordWithNoFragmentScriptIsCountedMissingAndTheDrainContinues()
    {
        var session = Session(out var hits);
        session.QueueRecordFragment(RecordFragmentScripts.InfoPrefix, "Info:Probe.esp:000999", 0x00000999, RecordFragmentScripts.InfoBegin);
        session.QueueRecordFragment(RecordFragmentScripts.InfoPrefix, Info, 0x00012345, RecordFragmentScripts.InfoEnd);
        session.RunQueuedFragments();
        Assert.Equal(new[] { 2 }, hits);
        Assert.Equal(1, session.RecordFragmentsMissing);
    }

    // Detects: the lookup matching on a partial id or accepting two scripts for one id; the match must use the low 24 bits and be unique.
    [Fact]
    public void ScriptLookupUsesTheLowIdBitsAndRefusesAmbiguity()
    {
        var names = new[] { "TIF__00012345", "TIF_MQ101_05012345", "TERM_X_00012345", "TIF__00012346" };
        Assert.Equal("TERM_X_00012345", RecordFragmentScripts.Find(names, RecordFragmentScripts.TerminalPrefix, 0x00012345));
        Assert.Null(RecordFragmentScripts.Find(names, RecordFragmentScripts.InfoPrefix, 0x00012345));
        Assert.Equal("TIF__00012346", RecordFragmentScripts.Find(names, RecordFragmentScripts.InfoPrefix, 0x00012346));
        Assert.Null(RecordFragmentScripts.Find(names, RecordFragmentScripts.InfoPrefix, 0x00000001));
        Assert.Equal("Fragment_Terminal_03", RecordFragmentScripts.TerminalFunction(3));
    }
}
