using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class FactionNativeAdaptersTests : HostFixture
{
    private const string Player = "000014:Base.esm";
    private const string Faction = "000F01:Base.esm";

    private IScriptHost Host(HostKind kind, EvidenceGate gate, string receiver, PapyrusValue faction, string body, List<int>? reported = null)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Faction"),
            TestHosts.Stub("Actor", null,
                "int Function GetFactionRank(Faction akFaction) native",
                "bool Function IsInFaction(Faction akFaction) native",
                "Function SetFactionRank(Faction akFaction, int aiRank) native",
                "Function RemoveFromFaction(Faction akFaction) native"),
            TestHosts.Stub("Source", null,
                "Actor Function Who() global native",
                "Faction Function Which() global native",
                "Function Report(int value) global native",
                "Function ReportFlag(bool value) global native"),
            new ScriptSpec("Caller",
                $"Scriptname Caller\nFunction Run()\n  Actor who = Source.Who()\n  Faction f = Source.Which()\n  {body}\nEndFunction\n")));
        host.RegisterNative(new NativeId("Source", "Who"), _ => PapyrusValue.FromObject(new EngineObjectReference(receiver)));
        host.RegisterNative(new NativeId("Source", "Which"), _ => faction);
        host.RegisterNative(new NativeId("Source", "Report"), arguments =>
        {
            reported?.Add((int)arguments[0].Integer);
            return PapyrusValue.None;
        });
        host.RegisterNative(new NativeId("Source", "ReportFlag"), arguments =>
        {
            reported?.Add(arguments[0].Flag ? 1 : 0);
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

    private static FactionNativeAdapters Adapters(IScriptHost host, EvidenceGate gate, List<string> log, Dictionary<string, int> ranks) =>
        new(host, gate, key => key == Player, key => ranks.GetValueOrDefault(key, -1),
            (key, rank) => { log.Add($"set {key} {rank}"); ranks[key] = rank; return true; },
            key => { log.Add("leave " + key); ranks[key] = -1; return true; });

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void SetRankAndRemoveReachTheStoreForThePlayer(HostKind kind)
    {
        var log = new List<string>();
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Player, Bare(Faction), "who.SetFactionRank(f, 2)\n  who.RemoveFromFaction(f)");
        var report = Adapters(host, gate, log, []).Bind();

        var result = Run(host);

        Assert.Equal(4, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { $"set {Faction} 2", $"leave {Faction}" }, log.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void RankAndMembershipAnswerFromTheStoreAndNonMembersAreMinusOne(HostKind kind)
    {
        var reported = new List<int>();
        var ranks = new Dictionary<string, int> { [Faction] = 3 };
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Player, Bare(Faction),
            "Source.Report(who.GetFactionRank(f))\n  Source.ReportFlag(who.IsInFaction(f))\n  who.RemoveFromFaction(f)\n  Source.Report(who.GetFactionRank(f))\n  Source.ReportFlag(who.IsInFaction(f))", reported);
        Adapters(host, gate, [], ranks).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { 3, 1, -1, 0 }, reported.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AFormOwnerIsTheSameFactionAsItsBareKey(HostKind kind)
    {
        var log = new List<string>();
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Player, PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(Faction))), "who.SetFactionRank(f, 0)");
        Adapters(host, gate, log, []).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { $"set {Faction} 0" }, log.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonPlayerReceiverIsRefusedAndTheStoreIsUntouched(HostKind kind)
    {
        var log = new List<string>();
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, "000AAA:Base.esm", Bare(Faction), "who.SetFactionRank(f, 1)");
        Adapters(host, gate, log, []).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(log);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonPlayerReadAnswersFromTheActorsBaseFactionList(HostKind kind)
    {
        var log = new List<string>();
        var reported = new List<int>();
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, "000AAA:Base.esm", Bare(Faction), "Source.Report(who.GetFactionRank(f))\n  Source.ReportFlag(who.IsInFaction(f))", reported);
        new FactionNativeAdapters(host, gate, key => key == Player, _ => -1, (key, rank) => true, key => true,
            (actor, faction) => actor == "000AAA:Base.esm" && faction == Faction ? 3 : null).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { 3, 1 }, reported.ToArray());
        Assert.Empty(log);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonPlayerReadWithAnUnreadableListFailsInsteadOfAnsweringNotAMember(HostKind kind)
    {
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, "000AAA:Base.esm", Bare(Faction), "Source.Report(who.GetFactionRank(f))");
        new FactionNativeAdapters(host, gate, key => key == Player, _ => -1, (key, rank) => true, key => true, (actor, faction) => null).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANegativeRankIsRefusedAndTheStoreIsUntouched(HostKind kind)
    {
        var log = new List<string>();
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Player, Bare(Faction), "who.SetFactionRank(f, -1)");
        Adapters(host, gate, log, []).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(log);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AFactionThatIsNotAFormKeyIsRefusedAndTheStoreIsUntouched(HostKind kind)
    {
        var log = new List<string>();
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Player, Bare("no-colon"), "who.SetFactionRank(f, 1)");
        Adapters(host, gate, log, []).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(log);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenGateStillBindsTheNatives(HostKind kind)
    {
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "UNPROVEN"));
        var host = Host(kind, gate, Player, Bare(Faction), "who.SetFactionRank(f, 1)");

        var report = Adapters(host, gate, [], []).Bind();

        Assert.Equal(4, report.Bound);
        Assert.All(report.Natives, outcome => Assert.Equal(AdapterState.Bound, outcome.State));
    }
}
