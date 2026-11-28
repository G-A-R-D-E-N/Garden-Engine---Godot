using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestNativeSelfTests : HostFixture
{
    private const string First = "000800:QuestProbe.esp";
    private const string Second = "000801:QuestProbe.esp";

    private static readonly ScriptSpec QuestStub = TestHosts.Stub("Quest", null,
        "Function Observe() native");

    private static readonly ScriptSpec ObserverStub = TestHosts.Stub("Observer", null,
        "Function Observe() global native");

    private static EvidenceGate Gate(bool identityAllowed = true) => identityAllowed
        ? TestHosts.Gate(("owner.engine_identity", "ENGINE_POLICY"),
            (QuestRuntime.StartupOrderingKey, "ENGINE_POLICY"))
        : TestHosts.GateDenying("owner.engine_identity", QuestRuntime.StartupOrderingKey);

    private static ScriptSpec Probe(string name, bool onInit, bool global) => new(name,
        $"Scriptname {name} extends Quest\n" +
        (onInit ? "Function OnInit()\n" : "Function OnProbe()\n") +
        (global ? "  Observer.Observe()\n" : "  Observe()\n") +
        "EndFunction\n");

    private static ScriptSpec[] Scripts(bool onInit, bool global) =>
        new[] { QuestStub, ObserverStub, Probe("QuestProbe", onInit, global), Probe("OtherProbe", onInit, global) };

    private static QuestStartupInput Input(string quest, params string[] scripts) => new(quest, true, scripts, 0, 0,
        Metadata: new QuestStartupMetadata(Array.Empty<QuestStartupProperty>(), "",
            Array.Empty<QuestStartupProperty>(), Array.Empty<QuestStartupFragment>(), Array.Empty<int>(),
            Array.Empty<QuestStartupAliasScript>(), Array.Empty<QuestStartupProperty>()));

    private GameSession Session(HostKind kind, EvidenceGate gate, bool onInit, bool global,
        params QuestStartupInput[] inputs)
    {
        var host = Track(TestHosts.Create(kind, gate, Scripts(onInit, global)));
        return new GameSession(host, gate, inputs);
    }

    public static IEnumerable<object[]> HostAndBool()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            yield return new object[] { host[0], false };
            yield return new object[] { host[0], true };
        }
    }

    public static IEnumerable<object[]> HostAndTwoBools()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            foreach (var onInit in new[] { false, true })
            {
                foreach (var sessionPath in new[] { false, true })
                {
                    yield return new object[] { host[0], onInit, sessionPath };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(HostAndTwoBools))]
    public void QuestStartupRetainsItsOwnerBeforeInitializationAndLaterNativeCalls(HostKind kind, bool onInit,
        bool sessionPath)
    {
        var session = Session(kind, Gate(), onInit, false, Input(First, "QuestProbe"));
        var observed = new List<PapyrusOwnerId>();
        session.Host.RegisterNative(new NativeId("Quest", "Observe"), (receiver, _) =>
        {
            observed.Add(Assert.IsType<EngineObjectReference>(receiver.Reference).Owner!.Value);
            return PapyrusValue.None;
        }, NativeTargetKind.EngineObject);
        var result = sessionPath ? session.StartNewGame()
            : session.Quests.StartNewGame(session.QuestStartup, session.Host);
        Assert.True(result.Applied, string.Join("; ", result.Reasons));
        var instance = Assert.Single(session.Quests.InstancesFor(First));
        if (!onInit) Assert.True(session.Host.RunEvent(instance, "OnProbe").Completed);
        Assert.Equal(PapyrusOwnerId.ForForm(First), Assert.Single(observed));
        Assert.Equal(PapyrusOwnerId.ForForm(First), instance.Owner);
        Assert.Equal(1, result.Started);
        Assert.Equal(1, result.AttachedScripts);
        Assert.Equal(0, TestHosts.NativeTargetRefusals(session.Host));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void MultipleScriptsKeepTheirQuestOwnerWithoutCollapsingOtherQuests(HostKind kind)
    {
        var session = Session(kind, Gate(), false, false,
            Input(First, "QuestProbe", "OtherProbe"), Input(Second, "QuestProbe"));
        var observed = new List<PapyrusOwnerId>();
        session.Host.RegisterNative(new NativeId("Quest", "Observe"), (receiver, _) =>
        {
            observed.Add(Assert.IsType<EngineObjectReference>(receiver.Reference).Owner!.Value);
            return PapyrusValue.None;
        }, NativeTargetKind.EngineObject);
        Assert.True(session.StartNewGame().Applied);
        foreach (var quest in new[] { First, Second })
            foreach (var instance in session.Quests.InstancesFor(quest))
                Assert.True(session.Host.RunEvent(instance, "OnProbe").Completed);
        Assert.Equal(new[] { PapyrusOwnerId.ForForm(First), PapyrusOwnerId.ForForm(First),
            PapyrusOwnerId.ForForm(Second) }, observed);
        Assert.Equal(2, session.Events.RegisteredOwners);
        Assert.Equal(3, session.Host.Instances.Count);
    }


    [Theory]
    [MemberData(nameof(HostAndBool))]
    public void OtherNativeTargetKindsKeepTheOriginalQuestScriptSelf(HostKind kind, bool global)
    {
        var session = Session(kind, Gate(), false, global, Input(First, "QuestProbe"));
        ScriptInstance? observed = null;
        session.Host.RegisterNative(new NativeId(global ? "Observer" : "Quest", "Observe"), (receiver, _) =>
        {
            observed = receiver.Reference as ScriptInstance;
            return PapyrusValue.None;
        }, global ? NativeTargetKind.Static : NativeTargetKind.ScriptInstance);
        Assert.True(session.StartNewGame().Applied);
        var instance = Assert.Single(session.Quests.InstancesFor(First));
        Assert.True(session.Host.RunEvent(instance, "OnProbe").Completed);
        if (!global)
        {
            Assert.Same(instance, observed);
        }
    }

}
