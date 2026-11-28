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

    private static FactionNativeAdapters Adapters(IScriptHost host, EvidenceGate gate, List<string> log, Dictionary<string, int> ranks, IActorFactions? actors = null) =>
        new(host, gate, key => key == Player, key => ranks.GetValueOrDefault(key, -1),
            (key, rank) => { log.Add($"set {key} {rank}"); ranks[key] = rank; return true; },
            key => { log.Add("leave " + key); ranks[key] = -1; return true; }, actors);

    private const string Npc = "000A00:Base.esm";
    private const string AddToFactionBody = "if (!who.IsInFaction(f))\n    who.SetFactionRank(f, 0)\n  endif";

    private sealed class FakeActors(Dictionary<string, int>? ranks = null) : IActorFactions
    {
        public Dictionary<string, int> Ranks { get; } = ranks ?? [];

        public List<string> Log { get; } = [];

        public bool Unreadable { get; init; }

        public int? Rank(string actor, string faction) => Unreadable ? null : Ranks.GetValueOrDefault($"{actor} {faction}", -1);

        public void SetRank(string actor, string faction, int rank)
        {
            Log.Add($"set {actor} {faction} {rank}");
            Ranks[$"{actor} {faction}"] = rank;
        }

        public void Leave(string actor, string faction)
        {
            Log.Add($"leave {actor} {faction}");
            Ranks[$"{actor} {faction}"] = -1;
        }
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonPlayerWriteGoesToTheActorStoreAndLeavesThePlayerStoreAlone(HostKind kind)
    {
        var log = new List<string>();
        var actors = new FakeActors();
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Npc, Bare(Faction), AddToFactionBody + "\n  who.SetFactionRank(f, 3)\n  who.RemoveFromFaction(f)");
        var report = Adapters(host, gate, log, [], actors).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { $"set {Npc} {Faction} 0", $"set {Npc} {Faction} 3", $"leave {Npc} {Faction}" }, actors.Log.ToArray());
        Assert.Empty(log);
        Assert.All(report.Natives, outcome => Assert.Contains("saved overlay", outcome.Reason, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonPlayerReadsRankAndMembershipFromTheActorStore(HostKind kind)
    {
        var reported = new List<int>();
        var actors = new FakeActors(new() { [$"{Npc} {Faction}"] = 4 });
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Npc, Bare(Faction),
            "Source.Report(who.GetFactionRank(f))\n  Source.ReportFlag(who.IsInFaction(f))\n  who.RemoveFromFaction(f)\n  Source.Report(who.GetFactionRank(f))\n  Source.ReportFlag(who.IsInFaction(f))", reported);
        Adapters(host, gate, [], [], actors).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { 4, 1, -1, 0 }, reported.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void JoiningAFactionTheNonPlayerAlreadyHasKeepsItsRank(HostKind kind)
    {
        var actors = new FakeActors(new() { [$"{Npc} {Faction}"] = 4 });
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Npc, Bare(Faction), AddToFactionBody);
        Adapters(host, gate, [], [], actors).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(actors.Log);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonPlayerWhoseBaseFactionsAreUnreadableIsRefusedAndNothingIsWritten(HostKind kind)
    {
        var actors = new FakeActors { Unreadable = true };
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Npc, Bare(Faction), AddToFactionBody);
        Adapters(host, gate, [], [], actors).Bind();

        var result = Run(host);

        Assert.False(result.Completed);
        Assert.Empty(actors.Log);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ThePlayerNeverReachesTheActorStoreWhenOneIsBound(HostKind kind)
    {
        var log = new List<string>();
        var actors = new FakeActors();
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Player, Bare(Faction), AddToFactionBody + "\n  who.SetFactionRank(f, 2)");
        Adapters(host, gate, log, [], actors).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { $"set {Faction} 0", $"set {Faction} 2" }, log.ToArray());
        Assert.Empty(actors.Log);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonPlayerWithoutAnActorStoreIsStillRefused(HostKind kind)
    {
        var log = new List<string>();
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Npc, Bare(Faction), "who.RemoveFromFaction(f)");
        var report = Adapters(host, gate, log, []).Bind();

        var result = Run(host);

        Assert.False(result.Completed);
        Assert.Empty(log);
        Assert.All(report.Natives, outcome => Assert.Contains("other actors are refused", outcome.Reason, StringComparison.Ordinal));
    }

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
        Assert.DoesNotContain(report.Natives, outcome => outcome.Family.Native.Function == "AddToFaction");
    }

    private const string RealActor =
        "Scriptname Actor\n" +
        "int Function GetFactionRank(Faction akFaction) native\n" +
        "bool Function IsInFaction(Faction akFaction) native\n" +
        "Function SetFactionRank(Faction akFaction, int aiRank) native\n" +
        "Function AddToFaction(Faction akFaction)\n" +
        "\tif (!IsInFaction(akFaction))\n" +
        "\t\tSetFactionRank(akFaction, 0)\n" +
        "\tendif\n" +
        "EndFunction\n";

    [Fact]
    public void TheRealActorAddToFactionBodyRunsOnAnEngineHandleThroughIsInFactionAndSetFactionRank()
    {
        var actors = new FakeActors();
        var reported = new List<int>();
        var gate = TestHosts.Gate((FactionNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Track(TestHosts.Create(HostKind.Radicle, gate,
            TestHosts.Stub("Faction"),
            new ScriptSpec("Actor", RealActor),
            TestHosts.Stub("Source", null, "Actor Function Who() global native", "Faction Function Which() global native", "Function Report(int value) global native"),
            new ScriptSpec("Caller", "Scriptname Caller\nFunction Run()\n  Actor who = Source.Who()\n  Faction f = Source.Which()\n  who.AddToFaction(f)\n  Source.Report(who.GetFactionRank(f))\n  who.SetFactionRank(f, 4)\n  who.AddToFaction(f)\n  Source.Report(who.GetFactionRank(f))\nEndFunction\n")));
        host.RegisterNative(new NativeId("Source", "Who"), _ => PapyrusValue.FromObject(new EngineObjectReference(Npc) { ScriptType = FormScriptTypes.For("ACHR") }));
        host.RegisterNative(new NativeId("Source", "Which"), _ => Bare(Faction));
        host.RegisterNative(new NativeId("Source", "Report"), arguments => { reported.Add((int)arguments[0].Integer); return PapyrusValue.None; });
        Adapters(host, gate, [], [], actors).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { $"set {Npc} {Faction} 0", $"set {Npc} {Faction} 4" }, actors.Log.ToArray());
        Assert.Equal(new[] { 0, 4 }, reported.ToArray());
    }
}
