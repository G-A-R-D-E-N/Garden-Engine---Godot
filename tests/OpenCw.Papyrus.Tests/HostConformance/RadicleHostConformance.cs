using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class RadicleHostConformance : ScriptHostConformance
{
    private const string TwoEvents =
        "Scriptname Probe\n" +
        "Int Function Park() global native\n" +
        "Function Sink(Int number) global native\n" +
        "Function Run()\n" +
        "  Int got = Park()\n" +
        "  Sink(got)\n" +
        "EndFunction\n" +
        "Function Other()\n" +
        "  Sink(2)\n" +
        "EndFunction\n";

    private const string SelfCount =
        "Scriptname Probe\n" +
        "Int Property Count Auto\n" +
        "Function Sink(Int number) global native\n" +
        "Function Read()\n" +
        "  Sink(Count)\n" +
        "EndFunction\n";

    private const string WriteOther =
        "Scriptname Probe\n" +
        "Int Property Count Auto\n" +
        "Function Sink(Int number) global native\n" +
        "Function Write(Probe target)\n" +
        "  target.Count = 9\n" +
        "EndFunction\n" +
        "Function Read(Probe target)\n" +
        "  Sink(target.Count)\n" +
        "EndFunction\n";

    protected override IScriptHost Create(IReadOnlyDictionary<string, byte[]> pex, EvidenceGate gate) =>
        new RadicleScriptHost(gate, pex.Values);

    [Fact]
    public void RunEventRunsOnlyItsOwnCall()
    {
        var host = Open(("Probe", TwoEvents));
        var received = new List<PapyrusValue>();
        host.RegisterNative(new NativeId("Probe", "Park"), _ => throw new NativeWait(7));
        host.RegisterNative(new NativeId("Probe", "Sink"), arguments =>
        {
            received.AddRange(arguments);
            return PapyrusValue.None;
        });
        var instance = Ready(host, "Probe");

        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
        Assert.True(host.CompleteLatent(7, PapyrusValue.FromInt(3)));

        var other = host.RunEvent(instance, "Other");
        Assert.True(other.Completed, Describe(other));
        Assert.Equal(2L, Assert.Single(received).Integer);

        for (var step = 0; step < 10 && received.Count < 2; ++step)
        {
            host.RunStep(512);
        }
        Assert.Equal(2, received.Count);
        Assert.Equal(3L, received[1].Integer);
    }

    [Fact]
    public void SameScriptReadsAttachedAutoProperty()
    {
        var host = Open(("Probe", SelfCount));
        var received = new List<PapyrusValue>();
        host.RegisterNative(new NativeId("Probe", "Sink"), arguments =>
        {
            received.AddRange(arguments);
            return PapyrusValue.None;
        });
        var properties = new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Count"] = PapyrusValue.FromInt(5),
        };
        var instance = Ready(host, "Probe", properties);

        var result = host.RunEvent(instance, "Read");

        Assert.True(result.Completed, Describe(result));
        Assert.Equal(5L, Assert.Single(received).Integer);
    }

    [Fact]
    public void ScriptWritesAnotherInstanceProperty()
    {
        var host = Open(("Probe", WriteOther));
        var received = new List<PapyrusValue>();
        host.RegisterNative(new NativeId("Probe", "Sink"), arguments =>
        {
            received.AddRange(arguments);
            return PapyrusValue.None;
        });
        var writer = Ready(host, "Probe");
        var target = Ready(host, "Probe");

        var wrote = host.RunEvent(writer, "Write", PapyrusValue.FromObject(target));
        Assert.True(wrote.Completed, Describe(wrote));

        var read = host.RunEvent(target, "Read", PapyrusValue.FromObject(target));
        Assert.True(read.Completed, Describe(read));
        Assert.Equal(9L, Assert.Single(received).Integer);
    }
}
