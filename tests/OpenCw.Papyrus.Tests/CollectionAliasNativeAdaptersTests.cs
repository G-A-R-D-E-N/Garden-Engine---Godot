using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class CollectionAliasNativeAdaptersTests : HostFixture
{
    private const string Quest = "027411:Test.esm";
    private const string RefA = "A1:Test.esm";
    private const string RefB = "B2:Test.esm";

    private static QuestAliasResolver Resolver() => new(_ => Array.Empty<AliasFillDef>(), _ => null);

    private IScriptHost Host(HostKind kind, EvidenceGate gate, string body)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("RefCollectionAlias", null,
                "int Function Find(ObjectReference akFindRef) native",
                "int Function GetCount() native",
                "ObjectReference Function GetAt(int aiIndex) native",
                "Function AddRef(ObjectReference akNewRef) native",
                "Function RemoveRef(ObjectReference akRemoveRef) native",
                "Function RemoveAll() native"),
            TestHosts.Stub("ObjectReference", null),
            TestHosts.Stub("Probe", null,
                "RefCollectionAlias Function Coll() global native",
                "ObjectReference Function A() global native",
                "ObjectReference Function B() global native",
                "Function Report(int value) global native"),
            new ScriptSpec("Caller", $"Scriptname Caller\nFunction Run()\n  RefCollectionAlias c = Probe.Coll()\n  ObjectReference a = Probe.A()\n  ObjectReference b = Probe.B()\n  {body}\nEndFunction\n")));
        host.RegisterNative(new NativeId("Probe", "Coll"), _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForQuestAlias(Quest, 7))));
        host.RegisterNative(new NativeId("Probe", "A"), _ => PapyrusValue.FromObject(new EngineObjectReference(RefA)));
        host.RegisterNative(new NativeId("Probe", "B"), _ => PapyrusValue.FromObject(new EngineObjectReference(RefB)));
        return host;
    }

    private static VmResult Run(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    private static void Reports(IScriptHost host, List<int> reported) =>
        host.RegisterNative(new NativeId("Probe", "Report"), arguments =>
        {
            reported.Add((int)arguments[0].Integer);
            return PapyrusValue.None;
        });

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AddFindCountRemoveAndRemoveAllWorkOnTheCollectionAndIgnoreDuplicates(HostKind kind)
    {
        var gate = TestHosts.Gate((AliasNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var reported = new List<int>();
        var host = Host(kind, gate,
            "Probe.Report(c.Find(a))\n  c.AddRef(a)\n  c.AddRef(a)\n  c.AddRef(b)\n  Probe.Report(c.Find(a))\n  Probe.Report(c.Find(b))\n  Probe.Report(c.GetCount())\n" +
            "  c.RemoveRef(a)\n  Probe.Report(c.Find(a))\n  Probe.Report(c.Find(b))\n  Probe.Report(c.GetCount())\n  c.RemoveAll()\n  Probe.Report(c.GetCount())");
        Reports(host, reported);
        var resolver = Resolver();
        var report = new CollectionAliasNativeAdapters(host, gate, resolver, key => new EngineObjectReference(key)).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { -1, 0, 1, 2, -1, 0, 1, 0 }, reported.ToArray());
        Assert.Equal(6, report.Natives.Count(outcome => outcome.State == AdapterState.Bound));
        Assert.DoesNotContain(report.Natives, outcome => outcome.Family.Native.Function is "GetActorAt" or "AddRefCollection");
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void GetAtReturnsTheMemberAndNoneOutOfRangeAndTheMembersBelongToTheirAlias(HostKind kind)
    {
        var gate = TestHosts.Gate((AliasNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var reported = new List<int>();
        var host = Host(kind, gate, "c.AddRef(b)\n  If c.GetAt(0) == b\n    Probe.Report(1)\n  EndIf\n  If c.GetAt(5) == None\n    Probe.Report(2)\n  EndIf\n  If c.GetAt(-1) == None\n    Probe.Report(3)\n  EndIf");
        Reports(host, reported);
        var resolver = Resolver();
        new CollectionAliasNativeAdapters(host, gate, resolver, key => new EngineObjectReference(key)).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { 1, 2, 3 }, reported.ToArray());
        Assert.Equal(new[] { RefB }, resolver.Members(Quest, 7));
        Assert.Empty(resolver.Members(Quest, 8));
        Assert.Empty(resolver.Members("027412:Test.esm", 7));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonAliasReceiverIsRefusedAndNothingIsStored(HostKind kind)
    {
        var gate = TestHosts.Gate((AliasNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var host = Host(kind, gate, "c.AddRef(a)");
        host.RegisterNative(new NativeId("Probe", "Coll"), _ => PapyrusValue.FromObject(new EngineObjectReference(RefB)));
        var resolver = Resolver();
        new CollectionAliasNativeAdapters(host, gate, resolver, key => new EngineObjectReference(key)).Bind();

        var result = Run(host);

        Assert.False(result.Completed);
        Assert.Empty(resolver.Members(Quest, 7));
    }

    private const string RealCollection =
        "Scriptname RefCollectionAlias\n" +
        "int Function Find(ObjectReference akFindRef) native\n" +
        "int Function GetCount() native\n" +
        "ObjectReference Function GetAt(int aiIndex) native\n" +
        "Function AddRef(ObjectReference akNewRef) native\n" +
        "Actor Function GetActorAt(int aiIndex)\n" +
        "\treturn GetAt(aiIndex) as Actor\n" +
        "endFunction\n" +
        "Function AddRefCollection(RefCollectionAlias refCollectionAliasToAdd)\n" +
        "\tint CollectionCount = refCollectionAliasToAdd.GetCount() const\n" +
        "\tint index = 0\n" +
        "\twhile index < CollectionCount\n" +
        "\t\taddRef(refCollectionAliasToAdd.getAt(index))\n" +
        "\t\tindex += 1\n" +
        "\tendWhile\n" +
        "endFunction\n";

    [Fact]
    public void TheRealGetActorAtAndAddRefCollectionBodiesRunOnEngineHandlesThroughGetAtGetCountAndAddRef()
    {
        var gate = TestHosts.Gate((AliasNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var reported = new List<int>();
        var host = Track(TestHosts.Create(HostKind.Radicle, gate,
            new ScriptSpec("RefCollectionAlias", RealCollection),
            TestHosts.Stub("Actor", "ObjectReference"),
            TestHosts.Stub("ObjectReference", null),
            TestHosts.Stub("Probe", null, "RefCollectionAlias Function Source() global native", "RefCollectionAlias Function Target() global native", "ObjectReference Function A() global native", "ObjectReference Function B() global native", "Function Report(int value) global native"),
            new ScriptSpec("Caller", "Scriptname Caller\nFunction Run()\n  RefCollectionAlias source = Probe.Source()\n  RefCollectionAlias target = Probe.Target()\n  source.AddRef(Probe.A())\n  source.AddRef(Probe.B())\n  Probe.Report(source.GetCount())\n  Actor first = source.GetActorAt(0)\n  If first != None\n    Probe.Report(1)\n  EndIf\n  Actor beyond = source.GetActorAt(9)\n  If beyond == None\n    Probe.Report(2)\n  EndIf\n  target.AddRefCollection(source)\n  Probe.Report(target.GetCount())\n  Probe.Report(target.Find(Probe.B()))\nEndFunction\n")));
        host.RegisterNative(new NativeId("Probe", "Source"), _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForQuestAlias(Quest, 7)) { ScriptType = "RefCollectionAlias" }));
        host.RegisterNative(new NativeId("Probe", "Target"), _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForQuestAlias(Quest, 8)) { ScriptType = "RefCollectionAlias" }));
        host.RegisterNative(new NativeId("Probe", "A"), _ => PapyrusValue.FromObject(new EngineObjectReference(RefA)));
        host.RegisterNative(new NativeId("Probe", "B"), _ => PapyrusValue.FromObject(new EngineObjectReference(RefB)));
        Reports(host, reported);
        var resolver = Resolver();
        new CollectionAliasNativeAdapters(host, gate, resolver, key => new EngineObjectReference(key) { ScriptType = "Actor" }).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { 2, 1, 2, 2, 1 }, reported.ToArray());
        Assert.Equal(new[] { RefA, RefB }, resolver.Members(Quest, 8));
    }

    [Fact]
    public void TheMembersRoundTripThroughTheSceneStateAndALoadReplacesThem()
    {
        var saved = Resolver();
        saved.AddMember(Quest, 7, RefA);
        saved.AddMember(Quest, 7, RefB);
        saved.AddMember(Quest, 9, RefA);

        var sections = SceneStateSave.Sections([], saved);
        var read = SceneStateSave.Read(sections);
        var loaded = Resolver();
        loaded.AddMember(Quest, 3, "C3:Test.esm");
        SceneStateSave.Restore(read, null, loaded, null);

        Assert.Equal(new[] { RefA, RefB }, loaded.Members(Quest, 7));
        Assert.Equal(new[] { RefA }, loaded.Members(Quest, 9));
        Assert.Empty(loaded.Members(Quest, 3));
    }

    [Fact]
    public void ASaveWithoutCollectionsLeavesNoMembersAndAnEmptyResolverWritesNoSection()
    {
        Assert.Empty(SceneStateSave.Sections([], Resolver()));
        var loaded = Resolver();
        loaded.AddMember(Quest, 7, RefA);

        SceneStateSave.Restore(SceneStateSave.Deserialize("{\"version\":1,\"runs\":[],\"aliases\":[]}"), null, loaded, null);

        Assert.Empty(loaded.Members(Quest, 7));
    }

    [Theory]
    [InlineData("{\"version\":1,\"runs\":[],\"aliases\":[],\"collections\":{}}")]
    [InlineData("{\"version\":1,\"runs\":[],\"aliases\":[],\"collections\":[{\"quest\":\"Q\",\"alias\":1}]}")]
    [InlineData("{\"version\":1,\"runs\":[],\"aliases\":[],\"collections\":[{\"quest\":\"Q\",\"alias\":1,\"refs\":[\"\"]}]}")]
    [InlineData("{\"version\":1,\"runs\":[],\"aliases\":[],\"collections\":[{\"quest\":\"Q\",\"alias\":1,\"refs\":[\"A1:T.esm\",\"a1:t.esm\"]}]}")]
    [InlineData("{\"version\":1,\"runs\":[],\"aliases\":[],\"collections\":[{\"quest\":\"Q\",\"alias\":1,\"refs\":[]},{\"quest\":\"Q\",\"alias\":1,\"refs\":[]}]}")]
    public void AMalformedCollectionSectionIsRefusedBeforeAnythingIsApplied(string json)
    {
        Assert.Throws<FormatException>(() => SceneStateSave.Deserialize(json));
    }
}
