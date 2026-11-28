using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Papyrus.Vmad;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class InfoFragmentSessionTests : HostFixture
{
    const string Script = "TIF_Probe";
    const string Speaker = "000014:Fallout4.esm";

    static readonly ScriptSpec[] Scripts =
    {
        TestHosts.Stub("ScriptObject"),
        TestHosts.Stub("Form"),
        TestHosts.Stub("ObjectReference", "Form"),
        TestHosts.Stub("Recorder", null, "Function Hit(Int tag) global native", "Function Missing() global native"),
        TestHosts.Stub(Script, null,
            "Int Property Count Auto",
            "Function Fragment_Begin(ObjectReference akSpeakerRef)\n  Recorder.Hit(Count + 100)\nEndFunction",
            "Function Fragment_End(ObjectReference akSpeakerRef)\n  Recorder.Hit(Count)\nEndFunction",
            "Function Fragment_Bad(ObjectReference akSpeakerRef)\n  Recorder.Missing()\nEndFunction"),
    };

    static InfoFragmentRow End(string function = "Fragment_End", string script = Script) => new(true, script, function);

    static InfoVmadParse Parse(params VmadValueShape[] properties) =>
        new(Script, properties, [End(), new InfoFragmentRow(false, Script, "Fragment_Begin")], null);

    GameSession Session(out List<long> hits)
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = new GameSession(Track(TestHosts.Create(HostKind.Radicle, gate, Scripts)), gate);
        var seen = new List<long>();
        session.Host.RegisterNative(new NativeId("Recorder", "Hit"), args =>
        {
            seen.Add(args[0].Integer);
            return PapyrusValue.None;
        });
        hits = seen;
        return session;
    }

    [Fact]
    public void TheRowRunsWithItsBoundPropertiesAndTheSpeakerArgument()
    {
        var session = Session(out var hits);
        var parse = Parse(new VmadValueShape("Count", "Int", Integer: 7));
        var (completed, detail) = session.RunInfoFragment("000900:Fallout4.esm", parse, End(), Speaker);
        Assert.True(completed, detail);
        Assert.Equal([7L], hits);
    }

    [Fact]
    public void TheScriptIsAttachedOncePerInfoAndEachRowRunsOnce()
    {
        var session = Session(out var hits);
        var parse = Parse(new VmadValueShape("Count", "Int", Integer: 7));
        Assert.True(session.RunInfoFragment("000900:Fallout4.esm", parse, parse.Rows[1], Speaker).Completed);
        Assert.True(session.RunInfoFragment("000900:Fallout4.esm", parse, parse.Rows[0], Speaker).Completed);
        Assert.Equal([107L, 7L], hits);
        Assert.Equal(1, session.Host.Instances.Count(i => i.ScriptName == Script));
        Assert.True(session.RunInfoFragment("000901:Fallout4.esm", parse, parse.Rows[0], Speaker).Completed);
        Assert.Equal(2, session.Host.Instances.Count(i => i.ScriptName == Script));
    }

    [Fact]
    public void AMissingScriptIsRefusedWithoutRunningAnything()
    {
        var session = Session(out var hits);
        var (completed, detail) = session.RunInfoFragment("000900:Fallout4.esm", Parse(), End(script: "TIF_Missing"), Speaker);
        Assert.False(completed);
        Assert.Contains("not available", detail);
        Assert.Empty(hits);
    }

    [Fact]
    public void AFunctionTheScriptLacksIsRefusedAndItsInstanceIsDiscarded()
    {
        var session = Session(out var hits);
        var (completed, detail) = session.RunInfoFragment("000900:Fallout4.esm", Parse(), End("Fragment_Nope"), Speaker);
        Assert.False(completed);
        Assert.Contains("did not complete", detail);
        Assert.Empty(hits);
        Assert.DoesNotContain(session.Host.Instances, i => i.ScriptName == Script);
    }

    [Fact]
    public void AFragmentWhoseBodyFailsIsRefusedWithTheHostReason()
    {
        var session = Session(out _);
        var (completed, detail) = session.RunInfoFragment("000900:Fallout4.esm", Parse(), End("Fragment_Bad"), Speaker);
        Assert.False(completed);
        Assert.Contains("did not complete", detail);
    }

    [Fact]
    public void WithoutTheSpeakerArgumentTheOneParameterFunctionDoesNotRun()
    {
        var session = Session(out var hits);
        var (completed, _) = session.RunInfoFragment("000900:Fallout4.esm", Parse(new VmadValueShape("Count", "Int", Integer: 7)), End(), null);
        Assert.False(completed);
        Assert.Empty(hits);
    }

    [Fact]
    public void APropertyShapeTheBinderCannotCarryIsRefused()
    {
        var session = Session(out var hits);
        var parse = Parse(new VmadValueShape("Count", "Struct", Members: null));
        var (completed, detail) = session.RunInfoFragment("000900:Fallout4.esm", parse, End(), Speaker);
        Assert.False(completed);
        Assert.Contains("cannot be bound", detail);
        Assert.Empty(hits);
    }
}
