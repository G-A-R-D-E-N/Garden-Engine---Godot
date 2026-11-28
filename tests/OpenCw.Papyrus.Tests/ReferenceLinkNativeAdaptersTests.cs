using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ReferenceLinkNativeAdaptersTests : HostFixture
{
    private const string Keyword = "000F00:Base.esm";
    private const string Linked = "000C01:Base.esm";
    private static readonly PapyrusOwnerId Source = PapyrusOwnerId.ForPlacedReference("000800:Base.esm", "000A00");

    private IScriptHost Host(HostKind kind, EvidenceGate gate, string keywordIdentity, string body, List<string> reported)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Keyword"),
            TestHosts.Stub("ObjectReference", null, "ObjectReference Function GetLinkedRef(Keyword apKeyword = None) native"),
            TestHosts.Stub("Probe", null,
                "ObjectReference Function Self() global native",
                "Keyword Function Kw() global native",
                "Function Report(ObjectReference value) global native"),
            new ScriptSpec("Caller", $"Scriptname Caller\nFunction Run()\n  ObjectReference target = Probe.Self()\n  {body}\nEndFunction\n")));
        host.RegisterNative(new NativeId("Probe", "Self"), _ => PapyrusValue.FromObject(new EngineObjectReference(Source)));
        host.RegisterNative(new NativeId("Probe", "Kw"), _ => PapyrusValue.FromObject(new EngineObjectReference(keywordIdentity)));
        host.RegisterNative(new NativeId("Probe", "Report"), arguments =>
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

    private static EvidenceGate Gate(string status = "APPROXIMATED") => TestHosts.Gate((ReferenceLinkNativeAdapters.SystemKey, status));

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheLinkForTheKeywordIsAskedByReferenceAndKeywordKeyAndTheTargetIsReturned(HostKind kind)
    {
        var asked = new List<(string Ref, string? Keyword)>();
        var reported = new List<string>();
        var gate = Gate();
        var host = Host(kind, gate, Keyword, "Probe.Report(target.GetLinkedRef(Probe.Kw()))", reported);
        var report = new ReferenceLinkNativeAdapters(host, gate, (refKey, keyword) => { asked.Add((refKey, keyword)); return Linked; }).Bind();

        var result = Run(host);

        Assert.Equal(1, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { ("000A00", (string?)Keyword) }, asked.ToArray());
        Assert.Equal(new[] { Linked }, reported.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ACallWithNoKeywordAsksForTheKeywordlessLink(HostKind kind)
    {
        var asked = new List<string?>();
        var reported = new List<string>();
        var gate = Gate();
        var host = Host(kind, gate, Keyword, "Probe.Report(target.GetLinkedRef())", reported);
        new ReferenceLinkNativeAdapters(host, gate, (_, keyword) => { asked.Add(keyword); return Linked; }).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new string?[] { null }, asked.ToArray());
        Assert.Equal(new[] { Linked }, reported.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AReferenceWithNoMatchingLinkAnswersNone(HostKind kind)
    {
        var reported = new List<string>();
        var gate = Gate();
        var host = Host(kind, gate, Keyword, "Probe.Report(target.GetLinkedRef(Probe.Kw()))", reported);
        new ReferenceLinkNativeAdapters(host, gate, (_, _) => null).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { "None" }, reported.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AKeywordThatIsNotAFormKeyIsRefusedAndNothingIsRead(HostKind kind)
    {
        var asked = 0;
        var reported = new List<string>();
        var gate = Gate();
        var host = Host(kind, gate, "no-colon", "Probe.Report(target.GetLinkedRef(Probe.Kw()))", reported);
        new ReferenceLinkNativeAdapters(host, gate, (_, _) => { asked++; return Linked; }).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(0, asked);
        Assert.Empty(reported);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnreadableReferenceFailsTheScriptInsteadOfAnsweringNone(HostKind kind)
    {
        var reported = new List<string>();
        var gate = Gate();
        var host = Host(kind, gate, Keyword, "Probe.Report(target.GetLinkedRef(Probe.Kw()))", reported);
        new ReferenceLinkNativeAdapters(host, gate, (_, _) => throw new InvalidOperationException("no load order is loaded")).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(reported);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void OnlyGetLinkedRefIsBoundAndAnUnprovenGateStillBinds(HostKind kind)
    {
        var gate = Gate("UNPROVEN");
        var host = Host(kind, gate, Keyword, "Probe.Report(target.GetLinkedRef())", new List<string>());

        var report = new ReferenceLinkNativeAdapters(host, gate, (_, _) => null).Bind();

        Assert.Equal(1, report.Bound);
        Assert.Single(report.Natives);
        Assert.All(report.Natives, outcome => Assert.Equal(AdapterState.Bound, outcome.State));
    }
}
