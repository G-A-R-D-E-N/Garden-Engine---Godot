using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestNativeSelfTests
{
    private const string First = "000800:QuestProbe.esp";
    private const string Second = "000801:QuestProbe.esp";

    private static EvidenceGate Gate(bool identityAllowed = true)
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.Parameters,
            EvidenceKeys.OnInitBarrier, EvidenceKeys.NativeTarget, EvidenceKeys.Dispatch,
            QuestRuntime.StartupOrderingKey, "owner.engine_identity" };
        return new EvidenceGate(EvidenceRegistry.Load(System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = EvidenceRegistry.SchemaName,
            rows = keys.Select(key => new { key, status = key == "owner.engine_identity" && !identityAllowed
                ? "UNPROVEN" : "ENGINE_POLICY" }),
        })));
    }

    private static QuestStartupInput Input(string quest, params string[] scripts) => new(quest, true, scripts, 0, 0,
        Metadata: new QuestStartupMetadata(Array.Empty<QuestStartupProperty>(), "",
            Array.Empty<QuestStartupProperty>(), Array.Empty<QuestStartupFragment>(), Array.Empty<int>(),
            Array.Empty<QuestStartupAliasScript>(), Array.Empty<QuestStartupProperty>()));

    private static ScriptLibrary Library(bool onInit = false, bool global = false)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("Quest", new[]
        {
            new PapyrusFunction("Observe", Array.Empty<PapyrusInstruction>(), IsNative: true),
        }, Array.Empty<string>()));
        var call = global ? PapyrusInstruction.CallStatic("Observer", "Observe", PapyrusOperand.Discard,
            Array.Empty<PapyrusOperand>()) : PapyrusInstruction.CallMethod(PapyrusOperand.SelfRef, "Observe",
                PapyrusOperand.Discard, Array.Empty<PapyrusOperand>(), receiverType: "Quest");
        foreach (var name in new[] { "QuestProbe", "OtherProbe" })
            library.Add(new PapyrusScript(name, new[]
            {
                new PapyrusFunction(onInit ? "OnInit" : "OnProbe", new[] { call, PapyrusInstruction.Return() }),
            }, Array.Empty<string>(), Parent: "Quest"));
        return library;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void QuestStartupRetainsItsOwnerBeforeInitializationAndLaterNativeCalls(bool onInit, bool sessionPath)
    {
        var gate = Gate();
        var session = new GameSession(Library(onInit), gate, new[] { Input(First, "QuestProbe") });
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
        Assert.Equal(0, ((ScriptHost)session.Host).NativeTargetRefusals);
    }

    [Fact]
    public void MultipleScriptsKeepTheirQuestOwnerWithoutCollapsingOtherQuests()
    {
        var session = new GameSession(Library(), Gate(), new[]
        {
            Input(First, "QuestProbe", "OtherProbe"), Input(Second, "QuestProbe"),
        });
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

    [Fact]
    public void RetainingAnOwnerDoesNotOpenTheIdentityGate()
    {
        var session = new GameSession(Library(), Gate(false), new[] { Input(First, "QuestProbe") });
        var calls = 0;
        session.Host.RegisterNative(new NativeId("Quest", "Observe"), (_, _) =>
        {
            calls++;
            return PapyrusValue.None;
        }, NativeTargetKind.EngineObject);
        Assert.True(session.StartNewGame().Applied);
        var result = session.Host.RunEvent(Assert.Single(session.Quests.InstancesFor(First)), "OnProbe");
        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains("owner.engine_identity", result.Message);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OtherNativeTargetKindsKeepTheOriginalQuestScriptSelf(bool global)
    {
        var session = new GameSession(Library(global: global), Gate(), new[] { Input(First, "QuestProbe") });
        ScriptInstance? observed = null;
        session.Host.RegisterNative(new NativeId(global ? "Observer" : "Quest", "Observe"), (receiver, _) =>
        {
            observed = Assert.IsType<ScriptInstance>(receiver.Reference);
            return PapyrusValue.None;
        }, global ? NativeTargetKind.Static : NativeTargetKind.ScriptInstance);
        Assert.True(session.StartNewGame().Applied);
        var instance = Assert.Single(session.Quests.InstancesFor(First));
        Assert.True(session.Host.RunEvent(instance, "OnProbe").Completed);
        Assert.Same(instance, observed);
    }
}
