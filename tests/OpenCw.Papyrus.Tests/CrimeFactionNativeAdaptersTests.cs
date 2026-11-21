using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class CrimeFactionNativeAdaptersTests : HostFixture
{
    private const string Receiver = "000AAA:Base.esm";
    private const string Faction = "000F01:Base.esm";

    private IScriptHost Host(HostKind kind, EvidenceGate gate, List<string> reported)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Faction"),
            TestHosts.Stub("Actor", null, "Faction Function GetCrimeFaction() native", "Function SetCrimeFaction(Faction akFaction) native"),
            TestHosts.Stub("Source", null,
                "Actor Function Who() global native",
                "Function Report(Faction value) global native"),
            new ScriptSpec("Caller",
                "Scriptname Caller\nFunction Run()\n  Actor who = Source.Who()\n  Faction found = who.GetCrimeFaction()\n  Source.Report(found)\nEndFunction\n")));
        host.RegisterNative(new NativeId("Source", "Who"), _ => PapyrusValue.FromObject(new EngineObjectReference(Receiver)));
        host.RegisterNative(new NativeId("Source", "Report"), arguments =>
        {
            reported.Add(arguments[0].Reference is EngineObjectReference reference ? reference.Identity : "None");
            return PapyrusValue.None;
        });
        return host;
    }

    private static VmResult Run(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheCrimeFactionOfTheReceiverIsReturnedAsAFormKey(HostKind kind)
    {
        var asked = new List<string>();
        var reported = new List<string>();
        var gate = TestHosts.Gate((CrimeFactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, reported);
        var report = new CrimeFactionNativeAdapters(host, gate, (string key, out string? faction) => { asked.Add(key); faction = Faction; return true; }).Bind();

        var result = Run(host);

        Assert.Equal(1, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { Receiver }, asked.ToArray());
        Assert.Equal(new[] { Faction }, reported.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnActorWithNoCrimeFactionAnswersNone(HostKind kind)
    {
        var reported = new List<string>();
        var gate = TestHosts.Gate((CrimeFactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, reported);
        new CrimeFactionNativeAdapters(host, gate, (string key, out string? faction) => { faction = null; return true; }).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { "None" }, reported.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUndecidableReceiverIsRefusedInsteadOfAnsweredNone(HostKind kind)
    {
        var reported = new List<string>();
        var gate = TestHosts.Gate((CrimeFactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, reported);
        new CrimeFactionNativeAdapters(host, gate, (string key, out string? faction) => { faction = null; return false; }).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(reported);
    }

    private IScriptHost SetHost(HostKind kind, EvidenceGate gate, string who, string? faction)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Faction"),
            TestHosts.Stub("Actor", null, "Faction Function GetCrimeFaction() native", "Function SetCrimeFaction(Faction akFaction) native"),
            TestHosts.Stub("Source", null,
                "Actor Function Who() global native",
                "Faction Function Fac() global native"),
            new ScriptSpec("Caller",
                "Scriptname Caller\nFunction Run()\n  Actor target = Source.Who()\n  target.SetCrimeFaction(Source.Fac())\nEndFunction\n")));
        host.RegisterNative(new NativeId("Source", "Who"), _ => PapyrusValue.FromObject(new EngineObjectReference(who)));
        host.RegisterNative(new NativeId("Source", "Fac"), _ => faction is null ? PapyrusValue.None : PapyrusValue.FromObject(new EngineObjectReference(faction)));
        return host;
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void SetCrimeFactionStoresTheFactionFormKeyForTheReceiver(HostKind kind)
    {
        var written = new List<(string Actor, string? Faction)>();
        var gate = TestHosts.Gate((CrimeFactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = SetHost(kind, gate, Receiver, Faction);
        var report = new CrimeFactionNativeAdapters(host, gate, (string key, out string? faction) => { faction = null; return true; }, (actor, faction) => written.Add((actor, faction))).Bind();

        var result = Run(host);

        Assert.Equal(2, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { (Receiver, (string?)Faction) }, written.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void SetCrimeFactionWithNoneStoresReportsToNobody(HostKind kind)
    {
        var written = new List<(string Actor, string? Faction)>();
        var gate = TestHosts.Gate((CrimeFactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = SetHost(kind, gate, Receiver, null);
        new CrimeFactionNativeAdapters(host, gate, (string key, out string? faction) => { faction = null; return true; }, (actor, faction) => written.Add((actor, faction))).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { (Receiver, (string?)null) }, written.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void SetCrimeFactionRefusesAReceiverThatIsNotAReferenceKeyAndStoresNothing(HostKind kind)
    {
        var written = new List<(string Actor, string? Faction)>();
        var gate = TestHosts.Gate((CrimeFactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = SetHost(kind, gate, "not-a-key", Faction);
        new CrimeFactionNativeAdapters(host, gate, (string key, out string? faction) => { faction = null; return true; }, (actor, faction) => written.Add((actor, faction))).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(written);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void WithoutAWriteStoreSetCrimeFactionStaysUnbound(HostKind kind)
    {
        var gate = TestHosts.Gate((CrimeFactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = SetHost(kind, gate, Receiver, Faction);

        var report = new CrimeFactionNativeAdapters(host, gate, (string key, out string? faction) => { faction = null; return true; }).Bind();

        Assert.Equal(1, report.Bound);
        Assert.DoesNotContain(report.Natives, outcome => outcome.Family.Native.Equals(CrimeFactionNativeAdapters.SetFamily.Native));
    }
}
