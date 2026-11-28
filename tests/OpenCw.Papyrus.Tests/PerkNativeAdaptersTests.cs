using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class PerkNativeAdaptersTests : HostFixture
{
    private const string Player = "000014:Base.esm";
    private const string Perk = "000F01:Base.esm";

    private IScriptHost Host(HostKind kind, EvidenceGate gate, string receiver, PapyrusValue perk, string body, List<bool>? reported = null, PapyrusOwnerId? owner = null)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Perk"),
            TestHosts.Stub("Actor", null,
                "Function AddPerk(Perk akPerk, bool abNotify = true) native\n" +
                "Function RemovePerk(Perk akPerk) native\n" +
                "bool Function HasPerk(Perk akPerk) native"),
            TestHosts.Stub("Source", null,
                "Actor Function Who() global native",
                "Perk Function Which() global native",
                "Function Report(bool value) global native"),
            new ScriptSpec("Caller",
                $"Scriptname Caller\nFunction Run()\n  Actor who = Source.Who()\n  Perk p = Source.Which()\n  {body}\nEndFunction\n")));
        host.RegisterNative(new NativeId("Source", "Who"), _ => PapyrusValue.FromObject(owner is { } carried ? new EngineObjectReference(carried) : new EngineObjectReference(receiver)));
        host.RegisterNative(new NativeId("Source", "Which"), _ => perk);
        host.RegisterNative(new NativeId("Source", "Report"), arguments =>
        {
            reported?.Add(arguments[0].Flag);
            return PapyrusValue.None;
        });
        return host;
    }

    private static PapyrusValue Bare(string key) => PapyrusValue.FromObject(new EngineObjectReference(key));

    private static VmResult Run(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    private static PerkNativeAdapters Adapters(IScriptHost host, EvidenceGate gate, List<string> log, HashSet<string> held) =>
        new(host, gate, key => key == Player, held.Contains,
            key => { log.Add("add " + key); return held.Add(key); },
            key => { log.Add("remove " + key); return held.Remove(key); });

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AddAndRemoveReachTheStoreForThePlayer(HostKind kind)
    {
        var log = new List<string>();
        var held = new HashSet<string>();
        var gate = TestHosts.Gate((PerkNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Player, Bare(Perk), "who.AddPerk(p)\n  who.AddPerk(p, False)\n  who.RemovePerk(p)");
        var report = Adapters(host, gate, log, held).Bind();

        var result = Run(host);

        Assert.Equal(3, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { "add " + Perk, "add " + Perk, "remove " + Perk }, log.ToArray());
        Assert.Empty(held);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void HasPerkAnswersFromTheStore(HostKind kind)
    {
        var reported = new List<bool>();
        var held = new HashSet<string> { Perk };
        var gate = TestHosts.Gate((PerkNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Player, Bare(Perk), "Source.Report(who.HasPerk(p))\n  who.RemovePerk(p)\n  Source.Report(who.HasPerk(p))", reported);
        Adapters(host, gate, [], held).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { true, false }, reported.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AFormOwnerIsTheSamePerkAsItsBareKey(HostKind kind)
    {
        var log = new List<string>();
        var gate = TestHosts.Gate((PerkNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Player, PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(Perk))), "who.AddPerk(p)");
        Adapters(host, gate, log, []).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { "add " + Perk }, log.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonPlayerReceiverIsRefusedAndTheStoreIsUntouched(HostKind kind)
    {
        var log = new List<string>();
        var gate = TestHosts.Gate((PerkNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, "000AAA:Base.esm", Bare(Perk), "who.AddPerk(p)");
        Adapters(host, gate, log, []).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(log);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void HasPerkOnANonPlayerAnswersFalseWithoutFailing(HostKind kind)
    {
        var reported = new List<bool>();
        var gate = TestHosts.Gate((PerkNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, "000AAA:Base.esm", Bare(Perk), "Source.Report(who.HasPerk(p))", reported);
        Adapters(host, gate, [], new HashSet<string> { Perk }).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { false }, reported.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void HasPerkOnAnAliasOwnedReceiverAnswersFalseWithoutFailing(HostKind kind)
    {
        var reported = new List<bool>();
        var gate = TestHosts.Gate((PerkNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Player, Bare(Perk), "Source.Report(who.HasPerk(p))", reported, PapyrusOwnerId.ForQuestAlias("000AAA:Base.esm", 3));
        Adapters(host, gate, [], new HashSet<string> { Perk }).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { false }, reported.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void APerkThatIsNotAFormKeyIsRefusedAndTheStoreIsUntouched(HostKind kind)
    {
        var log = new List<string>();
        var gate = TestHosts.Gate((PerkNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Player, Bare("no-colon"), "who.AddPerk(p)");
        Adapters(host, gate, log, []).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(log);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenGateStillBindsTheNatives(HostKind kind)
    {
        var gate = TestHosts.Gate((PerkNativeAdapters.SystemKey, "UNPROVEN"));
        var host = Host(kind, gate, Player, Bare(Perk), "who.AddPerk(p)");

        var report = Adapters(host, gate, [], []).Bind();

        Assert.Equal(3, report.Bound);
        Assert.All(report.Natives, outcome => Assert.Equal(AdapterState.Bound, outcome.State));
    }
}
