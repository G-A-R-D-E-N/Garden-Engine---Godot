using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;
using ConditionFunctions = OpenCommonwealth.ConditionFunctions;
using ConditionRule = Radicle.Interop.ConditionRule;
using DialogueRules = OpenCommonwealth.Services.Dialogue.DialogueRules;
using FormKey = Radicle.Interop.FormKey;
using RadicleEnvironment = Radicle.Interop.RadicleEnvironment;
using IQuestState = OpenCommonwealth.Services.Dialogue.IQuestState;
using QuestStateEntry = OpenCommonwealth.Services.Papyrus.Save.QuestStateEntry;
using QuestStateSnapshot = OpenCommonwealth.Services.Papyrus.Save.QuestStateSnapshot;
using SessionQuestState = OpenCommonwealth.Services.Dialogue.SessionQuestState;
using SessionRestore = OpenCommonwealth.Services.Papyrus.Save.SessionRestore;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestLifecycleSessionTests : HostFixture
{
    private const string Quest = "000800:QuestProbe.esp";

    private static readonly ScriptSpec[] Scripts =
    {
        TestHosts.Stub("ScriptObject"),
        TestHosts.Stub("Form"),
        TestHosts.Stub("QuestScript", null,
            "Int Property Count Auto",
            "Float Property SignedZero Auto",
            "Bool Property Flag Auto",
            "String Property Text Auto",
            "Form Property Link Auto",
            "Function OnInit()\nEndFunction",
            "Function OnProbe()\nEndFunction"),
    };

    private static readonly ScriptSpec[] FragmentScripts = Scripts.Concat(new[]
    {
        TestHosts.Stub("Quest", null,
            "Function Start() native", "Function Stop() native", "Function Reset() native", "Function CompleteQuest() native",
            "Function SetActive(Bool abActive) native", "Bool Function SetStage(Int aiStage) native"),
        TestHosts.Stub("QuestSource", null, "Quest Function Probe() global native"),
        TestHosts.Stub("Recorder", null, "Function Hit(Int stage, Int entry) global native"),
        TestHosts.Stub("QF_Probe", "Quest",
            "Function Fragment_Stage_0010_Item_00()\n  Recorder.Hit(10, 0)\nEndFunction",
            "Function Fragment_Stage_0010_Item_01()\n  Recorder.Hit(10, 1)\nEndFunction",
            "Function Fragment_Stage_0020_Item_00()\n  Recorder.Hit(20, 0)\nEndFunction"),
        new ScriptSpec("Caller", "Scriptname Caller\nFunction Advance(Int stage)\n  Quest target = QuestSource.Probe()\n  target.SetStage(stage)\nEndFunction\n"),
    }).ToArray();

    private static QuestStartupFragment Fragment(int stage, int entry, string? function = null) =>
        new((ushort)stage, entry, "QF_Probe", function ?? $"Fragment_Stage_{stage:D4}_Item_{entry:D2}");

    private GameSession FragmentSession(out List<(int, int)> hits, QuestStartupStage[]? stages, params QuestStartupFragment[] fragments)
    {
        var input = Scalar(stages ?? new[]
        {
            new QuestStartupStage(10, false, fragments.Count(fragment => fragment.Stage == 10)),
            new QuestStartupStage(20, false, fragments.Count(fragment => fragment.Stage == 20)),
        });
        input = input with { Fragments = fragments.Length, Metadata = input.Metadata! with { FragmentScript = "QF_Probe", Fragments = fragments } };
        var session = Session(QuestStartupBindingTests.Gate(), FragmentScripts, input);
        var seen = new List<(int, int)>();
        session.Host.RegisterNative(new NativeId("Recorder", "Hit"), args =>
        {
            seen.Add(((int)args[0].Integer, (int)args[1].Integer));
            return PapyrusValue.None;
        });
        session.Host.RegisterNative(new NativeId("QuestSource", "Probe"),
            _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(Quest))));
        session.BindQuestNatives(new[] { Quest });
        Start(session);
        hits = seen;
        return session;
    }

    private static VmResult Advance(GameSession session, int stage)
    {
        var caller = session.Host.Instances.FirstOrDefault(instance => instance.ScriptName == "Caller") ?? session.Host.Attach("Caller");
        Assert.True(session.Host.Initialize(caller).Completed);
        return session.Host.RunEvent(caller, "Advance", PapyrusValue.FromInt(stage));
    }

    // Detects: a Quest.SetStage from a script that runs no fragment, runs one inside the native callback, or runs a done stage's fragment again; fragments must run after the call returns, once, in log entry order.
    [Fact]
    public void AScriptSetStageRunsTheBoundFragmentsAfterTheCallReturnsAndOnlyOnce()
    {
        var session = FragmentSession(out var hits, null, Fragment(10, 1), Fragment(10, 0), Fragment(20, 0));
        Assert.True(Advance(session, 10).Completed);
        Assert.Empty(hits);
        Assert.Equal(2, session.RunQueuedFragments());
        Assert.Equal(new[] { (10, 0), (10, 1) }, hits);
        Assert.True(Advance(session, 10).Completed);
        Assert.Equal(0, session.RunQueuedFragments());
        Assert.True(Advance(session, 20).Completed);
        Assert.Equal(1, session.RunQueuedFragments());
        Assert.Equal(new[] { (10, 0), (10, 1), (20, 0) }, hits);
        Assert.Equal(3, session.FragmentsRun);
        Assert.Equal(0, session.FragmentsRefused);
    }

    // Detects: a stage set that skips or double-fires the one bound (stage, index) pair, or runs a fragment for an unbound index; only bound pairs must run (the append side is covered in QuestWritebackTests).
    [Fact]
    public void AStageSetRunsTheOneBoundPairExactlyOnce()
    {
        var session = FragmentSession(out var hits, null, Fragment(10, 0));
        Assert.True(Advance(session, 10).Completed);
        Assert.Equal(1, session.RunQueuedFragments());
        Assert.Equal(new[] { (10, 0) }, hits);
        Assert.Equal(0, session.FragmentsRefused);
    }

    // Detects: a startup stage that is seeded as done but whose fragment never runs, or runs again on a second drain; the fragment must run once after the start.
    [Fact]
    public void AStartupStageFragmentRunsOnceAfterStart()
    {
        var session = FragmentSession(out var hits, new[] { new QuestStartupStage(10, true, 1) }, Fragment(10, 0));
        Assert.Equal(10, session.QuestStages.StageOf(Quest));
        Assert.Empty(hits);
        Assert.Equal(1, session.RunQueuedFragments());
        Assert.Equal(new[] { (10, 0) }, hits);
        Assert.Equal(0, session.RunQueuedFragments());
    }

    // Detects: a fragment bound to a function the script lacks that undoes the stage or the quest start, or stops its neighbour; it must only be counted as refused.
    [Fact]
    public void AFragmentThatCannotRunIsCountedAndKeepsTheStage()
    {
        var session = FragmentSession(out var hits, null, Fragment(10, 0, "Fragment_Missing"), Fragment(10, 1));
        Assert.True(Advance(session, 10).Completed);
        Assert.Equal(1, session.RunQueuedFragments());
        Assert.Equal(new[] { (10, 1) }, hits);
        Assert.Equal(1, session.FragmentsRefused);
        Assert.Equal(10, session.QuestStages.StageOf(Quest));
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(Quest));
    }

    private sealed class FaultingHost : IScriptHost
    {
        private readonly IScriptHost _inner;
        public bool FaultInitialize, FaultRunEvent;
        public int Faults;
        public FaultingHost(IScriptHost inner) => _inner = inner;
        public EvidenceGate Gate => _inner.Gate;
        public ScriptOverrideRegistry? Overrides { get => _inner.Overrides; set => _inner.Overrides = value; }
        public IReadOnlyList<ScriptInstance> Instances => _inner.Instances;
        public int WaitingCount => _inner.WaitingCount;
        public ScriptInstance Attach(string scriptName, IReadOnlyDictionary<string, PapyrusValue>? properties = null,
            PapyrusOwnerId? owner = null, PapyrusOwnerId? stateIdentity = null) => _inner.Attach(scriptName, properties, owner, stateIdentity);
        public void Discard(ScriptInstance instance) => _inner.Discard(instance);
        public VmResult Initialize(ScriptInstance instance) =>
            FaultInitialize && instance.ScriptName == "QF_Probe" ? Fault() : _inner.Initialize(instance);
        public VmResult RunEvent(ScriptInstance instance, string functionName, params PapyrusValue[] arguments) =>
            FaultRunEvent && instance.ScriptName == "QF_Probe" ? Fault() : _inner.RunEvent(instance, functionName, arguments);
        private VmResult Fault() { Faults++; throw new NotSupportedException("probe"); }
        public void RegisterNative(NativeId native, Func<IReadOnlyList<PapyrusValue>, PapyrusValue> body, NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) => _inner.RegisterNative(native, body, kind, requiredScript);
        public void RegisterNative(NativeId native, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> body, NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) => _inner.RegisterNative(native, body, kind, requiredScript);
        public bool TryRegisterNative(NativeId native, Func<IReadOnlyList<PapyrusValue>, PapyrusValue> body, NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) => _inner.TryRegisterNative(native, body, kind, requiredScript);
        public bool TryRegisterNative(NativeId native, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> body, NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) => _inner.TryRegisterNative(native, body, kind, requiredScript);
        public bool HasScript(string name) => _inner.HasScript(name);
        public bool HasInvalidParentChain(string name) => _inner.HasInvalidParentChain(name);
        public int RunStep(int budget) => _inner.RunStep(budget);
        public bool CompleteLatent(long token, PapyrusValue value) => _inner.CompleteLatent(token, value);
    }

    private (GameSession Session, FaultingHost Host) FaultingFragmentSession()
    {
        var input = Scalar(new QuestStartupStage(10, false, 1));
        var fragment = Fragment(10, 0);
        input = input with { Fragments = 1, Metadata = input.Metadata! with { FragmentScript = "QF_Probe", Fragments = new[] { fragment } } };
        var gate = QuestStartupBindingTests.Gate();
        var host = new FaultingHost(Track(TestHosts.Create(HostKind.Radicle, gate, FragmentScripts)));
        var session = new GameSession(host, gate, new[] { input });
        host.RegisterNative(new NativeId("Recorder", "Hit"), _ => PapyrusValue.None);
        host.RegisterNative(new NativeId("QuestSource", "Probe"),
            _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(Quest))));
        session.BindQuestNatives(new[] { Quest });
        Start(session);
        Assert.DoesNotContain(host.Instances, instance => instance.ScriptName == "QF_Probe");
        return (session, host);
    }

    // Detects: a fragment script whose Initialize throws outside the host's own error types leaving its attached instance in the host; it must be discarded and counted as refused.
    [Fact]
    public void AFragmentWhoseInitializeThrowsAnUnexpectedExceptionDiscardsItsInstance()
    {
        var (session, host) = FaultingFragmentSession();
        host.FaultInitialize = true;
        Assert.True(Advance(session, 10).Completed);
        Assert.Equal(0, session.RunQueuedFragments());
        Assert.Equal(1, session.FragmentsRefused);
        Assert.Equal(1, host.Faults);
        Assert.DoesNotContain(host.Instances, instance => instance.ScriptName == "QF_Probe");
    }

    // Detects: a fragment event that throws outside the host's own error types leaving the instance the session attached for it in the host; it must be discarded and counted as refused.
    [Fact]
    public void AFragmentThatThrowsAnUnexpectedExceptionDiscardsItsInstance()
    {
        var (session, host) = FaultingFragmentSession();
        host.FaultRunEvent = true;
        Assert.True(Advance(session, 10).Completed);
        Assert.Equal(0, session.RunQueuedFragments());
        Assert.Equal(1, session.FragmentsRefused);
        Assert.Equal(1, host.Faults);
        Assert.DoesNotContain(host.Instances, instance => instance.ScriptName == "QF_Probe");
    }

    private static QuestStartupInput Scalar(params QuestStartupStage[] stages)
    {
        var input = QuestStartupBindingTests.Input();
        return input with
        {
            Metadata = input.Metadata! with { ScriptProperties = input.Metadata.ScriptProperties.Take(4).ToArray() },
            Stages = stages,
        };
    }

    private GameSession Session(EvidenceGate gate, params QuestStartupInput[] startup) =>
        new(Track(TestHosts.Create(HostKind.Radicle, gate, Scripts)), gate, startup);

    private GameSession Session(EvidenceGate gate, ScriptSpec[] scripts, params QuestStartupInput[] startup) =>
        new(Track(TestHosts.Create(HostKind.Radicle, gate, scripts)), gate, startup);

    private static object? Field(RadicleScriptHost host, string name) =>
        typeof(RadicleScriptHost).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host);

    private static bool LifecycleWasUsed(GameSession session) => Field((RadicleScriptHost)session.Host, "_lifecycle") is not null;

    private static int LatentTokens(RadicleScriptHost host) => ((System.Collections.IDictionary)Field(host, "_latentTokens")!).Count;

    private static void DisposeLifecycle(RadicleScriptHost host) => ((IDisposable)Field(host, "_lifecycle")!).Dispose();

    private static void Start(GameSession session)
    {
        var result = session.StartNewGame();
        Assert.True(result.Applied, string.Join("; ", result.Reasons));
    }

    private static int NativeInstances(GameSession session) => ((RadicleScriptHost)session.Host).Runtime.ListInstances().Count;

    [Fact]
    public void AScalarQuestStartsThroughTheLifecycleAndItsInstanceIsAdopted()
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = Session(gate, Scalar());
        session.Host.Overrides = new ScriptOverrideRegistry(gate);
        IReadOnlyDictionary<string, PapyrusValue>? seen = null;
        session.Host.Overrides.Register("QuestScript", "OnInit", 0, args =>
        {
            seen = args.Instance.Properties;
            return OverrideResult.Handled(PapyrusValue.None);
        });
        Start(session);
        Assert.Equal(17, seen!["Count"].Integer);
        var instance = Assert.Single(session.Quests.InstancesFor(Quest));
        Assert.Same(instance, Assert.Single(session.Host.Instances));
        Assert.Same(seen, instance.Properties);
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
        Assert.Equal(1, NativeInstances(session));
        Assert.Equal(1, session.Events.RegisteredOwners);
        Assert.True(session.Host.RunEvent(instance, "OnProbe").Completed);
    }

    [Fact]
    public void AQuestWithAnObjectPropertyStaysOnTheDirectPath()
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = Session(gate, QuestStartupBindingTests.Input());
        Start(session);
        Assert.Equal(1, session.Quests.StartedQuests);
    }

    [Fact]
    public void ALifecycleInitFailureRollsTheQuestBackAndStaysRetained()
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = Session(gate, Scalar());
        session.Host.Overrides = new ScriptOverrideRegistry(gate);
        var attempts = 0;
        session.Host.Overrides.Register("QuestScript", "OnInit", 0, _ =>
        {
            attempts++;
            return OverrideResult.Refused("synthetic initialization failure");
        });
        Assert.False(session.StartNewGame().Applied);
        Assert.False(session.StartNewGame().Applied);
        Assert.Equal(1, attempts);
        Assert.Equal(0, session.Quests.StartedQuests);
        Assert.Equal(0, session.Quests.AttachedScripts);
        Assert.Equal(1, session.Quests.BlockedQuests);
        Assert.True(LifecycleWasUsed(session));
        var instance = Assert.Single(session.Host.Instances);
        Assert.Equal(ScriptLifecycle.Failed, instance.Lifecycle);
        Assert.True(instance.Detached);
    }

    [Fact]
    public void ALaterQuestFailureLeavesTheEarlierLifecycleQuestRunning()
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = Session(gate, Scalar() with { Quest = "000700:QuestProbe.esp" }, Scalar());
        session.Host.Overrides = new ScriptOverrideRegistry(gate);
        session.Host.Overrides.Register("QuestScript", "OnInit", 0, args => args.Instance.Owner!.Value.BaseFormKey == Quest
            ? OverrideResult.Refused("synthetic initialization failure") : OverrideResult.Handled(PapyrusValue.None));
        var result = session.StartNewGame();
        Assert.True(result.Applied);
        Assert.Equal(new[] { "000700:QuestProbe.esp" }, session.Quests.Started);
        Assert.Equal(1, session.Quests.AttachedScripts);
        Assert.Equal(Quest, Assert.Single(result.Blocked).Quest);
        Assert.True(LifecycleWasUsed(session));
        Assert.Equal(2, session.Host.Instances.Count);
        var earlier = Assert.Single(session.Quests.InstancesFor("000700:QuestProbe.esp"));
        Assert.False(earlier.Detached);
        Assert.True(session.Host.RunEvent(earlier, "OnProbe").Completed);
        Assert.Empty(session.Quests.InstancesFor(Quest));
        Assert.True(Assert.Single(session.Host.Instances, instance => instance.Lifecycle == ScriptLifecycle.Failed).Detached);
    }

    [Theory]
    [InlineData(true, 20)]
    [InlineData(false, 20)]
    public void TheStartupFlaggedStageIsSeededForAStartedQuest(bool scalar, int expected)
    {
        var gate = QuestStartupBindingTests.Gate();
        var stages = new[] { new QuestStartupStage(10, false, 0), new QuestStartupStage(20, true, 0) };
        var input = scalar ? Scalar(stages) : QuestStartupBindingTests.Input() with { Stages = stages };
        var session = Session(gate, input);
        session.BindQuestNatives(new[] { Quest });
        Start(session);
        Assert.Equal(expected, session.QuestStages.StageOf(Quest));
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(Quest));
        Assert.True(session.QuestStages.StageDone(Quest, expected));
        Assert.Equal(1, session.SeededStages);
        Assert.Equal(expected, Assert.Single(session.StartupStages!.Stages).Value);
    }

    [Fact]
    public void AQuestRuntimeGivenARadicleHostStartsScalarQuestsThroughItsLifecycle()
    {
        var gate = QuestStartupBindingTests.Gate();
        var host = Track(TestHosts.Create(HostKind.Radicle, gate, Scripts));
        var runtime = new QuestRuntime(gate);
        Assert.True(runtime.StartNewGame(new[] { Scalar() }, host).Applied);
        Assert.Equal(1, ((RadicleScriptHost)host).Runtime.ListInstances().Count);
    }

    [Fact]
    public void AQuestWhoseParentChainDoesNotEndInALoadedRootGoesDirectWithItsPropertiesIntact()
    {
        var gate = QuestStartupBindingTests.Gate();
        var scripts = new[]
        {
            TestHosts.Stub("Form"),
            TestHosts.Stub("QuestScript", "ScriptObject",
                "Int Property Count Auto", "Float Property SignedZero Auto", "Bool Property Flag Auto",
                "String Property Text Auto", "Form Property Link Auto", "Function OnInit()\nEndFunction"),
        };
        var session = Session(gate, scripts, Scalar());
        var host = (RadicleScriptHost)session.Host;
        Assert.False(host.ChainEndsInLoadedRoot("QuestScript"));
        Assert.False(host.HasInvalidParentChain("QuestScript"));
        Start(session);
        Assert.Equal(1, session.Quests.StartedQuests);
        var instance = Assert.Single(session.Quests.InstancesFor(Quest));
        Assert.Equal(17, instance.Properties["Count"].Integer);
        Assert.Equal("a text", instance.Properties["Text"].Text);
        Assert.False(instance.Properties["Flag"].Flag);
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
    }

    [Fact]
    public void ASecondStartNewGameAfterSuccessDoesNotReseedOrRecount()
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = Session(gate, Scalar(new QuestStartupStage(10, true, 0)));
        session.BindQuestNatives(new[] { Quest });
        Start(session);
        Start(session);
        Assert.Equal(1, session.SeededStages);
        Assert.Equal(1, session.Quests.AttachedScripts);
        Assert.Equal(1, session.QuestStages.StageChanges);
        Assert.Equal(1, NativeInstances(session));
    }

    // Constraint: detects a startup seed built from quest record bytes that names a stage index its own record does not store.
    [Fact]
    public void TheStartupStageIsSeededFromQuestRecordBytesThroughBuild()
    {
        var directory = Directory.CreateTempSubdirectory("garden-quest-seed-");
        try
        {
            var path = Path.Combine(directory.FullName, QuestRecordBytes.PluginName);
            File.WriteAllBytes(path, QuestRecordBytes.StartupQuest());
            using var environment = RadicleEnvironment.Load(new[] { path });
            var definition = Assert.Single(QuestDefinition.ReadAll(environment));
            var input = Assert.Single(RadicleQuestStartup.Build(environment));
            Assert.Equal(Quest, input.Quest);
            Assert.Equal(definition.Stages.Select(stage => (int)stage.Index), input.Stages!.Select(stage => stage.Index));
            Assert.Equal(new[] { false, true }, input.Stages!.Select(stage => stage.IsStartup));
            var session = Session(QuestStartupBindingTests.Gate(), input);
            session.BindQuestNatives(new[] { Quest });
            Start(session);
            Assert.Equal(20, session.QuestStages.StageOf(Quest));
            Assert.Equal(1, session.SeededStages);
            Assert.Equal(20, Assert.Single(session.StartupStages!.Stages).Value);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void AQuestAlreadyPastNotStartedIsNotSeeded()
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = Session(gate, Scalar(new QuestStartupStage(10, true, 0)));
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.Start));
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.Stop));
        Assert.Equal(QuestState.Stopped, session.QuestStages.StateOf(Quest));
        Start(session);
        Assert.Equal(0, session.SeededStages);
        Assert.Equal(QuestState.Stopped, session.QuestStages.StateOf(Quest));
        Assert.Equal(0, session.QuestStages.StageOf(Quest));
        Assert.Equal(10, Assert.Single(session.StartupStages!.Stages).Value);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    public void AFlaggedStageWithFragmentsRecordsRelianceOnTheUnprovenDispatchTiming(int fragments, int stops)
    {
        var gate = TestHosts.GateDenying(QuestRuntime.FragmentTimingKey);
        var session = Session(gate, Scalar(new QuestStartupStage(10, false, 0), new QuestStartupStage(20, true, fragments)));
        Start(session);
        Assert.Equal(1, session.SeededStages);
        Assert.Equal(20, session.QuestStages.StageOf(Quest));
        Assert.Equal(stops, gate.Stops[QuestRuntime.FragmentTimingKey]);
    }

    [Fact]
    public void AnOnInitThatWaitsFailsTheLifecycleStartAndLeavesNoLatentToken()
    {
        var gate = QuestStartupBindingTests.Gate();
        var scripts = new[]
        {
            TestHosts.Stub("ScriptObject"),
            TestHosts.Stub("Form"),
            new ScriptSpec("External", "Scriptname External\nInt Function Answer() global native\n"),
            TestHosts.Stub("QuestScript", null,
                "Int Property Count Auto", "Float Property SignedZero Auto", "Bool Property Flag Auto",
                "String Property Text Auto", "Form Property Link Auto",
                "Function OnInit()\n  Int got = External.Answer()\nEndFunction"),
        };
        var session = Session(gate, scripts, Scalar());
        var host = (RadicleScriptHost)session.Host;
        host.RegisterNative(new NativeId("External", "Answer"), _ => throw new NativeWait(5), NativeTargetKind.Static);
        Assert.False(session.StartNewGame().Applied);
        Assert.True(LifecycleWasUsed(session));
        Assert.Equal(0, host.WaitingCount);
        Assert.Equal(0, LatentTokens(host));
    }

    [Fact]
    public void AHostWhoseRuntimeIsGoneRefusesAQuestStartInsteadOfThrowing()
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = Session(gate, Scalar());
        var host = (RadicleScriptHost)session.Host;
        host.Dispose();
        var start = host.StartQuest(Quest, new[] { "QuestScript" }, Array.Empty<Radicle.Interop.QuestLifecycleProperty>(), 0, 0,
            new Dictionary<string, Dictionary<string, PapyrusValue>>());
        Assert.False(start.Applied);
        Assert.StartsWith(Quest + ": ", start.Reason, StringComparison.Ordinal);
        Assert.Contains("PapyrusRuntime", start.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADirectQuestFailureAfterTheLifecycleIsGoneRollsBackOnlyItself()
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = Session(gate, Scalar() with { Quest = "000700:QuestProbe.esp" }, QuestStartupBindingTests.Input());
        var host = (RadicleScriptHost)session.Host;
        host.Overrides = new ScriptOverrideRegistry(gate);
        host.Overrides.Register("QuestScript", "OnInit", 0, args =>
        {
            if (args.Instance.Owner!.Value.BaseFormKey != Quest) return OverrideResult.Handled(PapyrusValue.None);
            DisposeLifecycle(host);
            return OverrideResult.Refused("synthetic initialization failure");
        });
        var result = session.StartNewGame();
        Assert.True(result.Applied);
        Assert.Equal(new[] { "000700:QuestProbe.esp" }, session.Quests.Started);
        Assert.Equal(Quest, Assert.Single(result.Blocked).Quest);
        var earlier = Assert.Single(session.Quests.InstancesFor("000700:QuestProbe.esp"));
        Assert.False(earlier.Detached);
        Assert.Equal(ScriptLifecycle.Ready, earlier.Lifecycle);
        Assert.Empty(session.Quests.InstancesFor(Quest));
    }

    [Fact]
    public void AStopErrorWhileRollingBackALifecycleQuestIsReportedAndLeavesTheOthersStarted()
    {
        var gate = QuestStartupBindingTests.Gate();
        var disposer = QuestStartupBindingTests.Input() with { Quest = "000750:QuestProbe.esp" };
        var session = Session(gate, Scalar() with { Quest = "000700:QuestProbe.esp" }, disposer, Scalar());
        var host = (RadicleScriptHost)session.Host;
        host.Overrides = new ScriptOverrideRegistry(gate);
        host.Overrides.Register("QuestScript", "OnInit", 0, args =>
        {
            if (args.Instance.Owner!.Value.BaseFormKey == disposer.Quest) DisposeLifecycle(host);
            return OverrideResult.Handled(PapyrusValue.None);
        });
        var result = session.StartNewGame();
        Assert.True(result.Applied);
        Assert.Equal(new[] { "000700:QuestProbe.esp", disposer.Quest }, session.Quests.Started);
        Assert.Equal(Quest, Assert.Single(result.Blocked).Quest);
        Assert.Empty(session.Quests.InstancesFor(Quest));
        Assert.Contains(result.Reasons, reason => reason.StartsWith(Quest + ": stop failed: ", StringComparison.Ordinal));
        Assert.All(session.Quests.Started, quest => Assert.All(session.Quests.InstancesFor(quest), instance => Assert.False(instance.Detached)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoStageIsSeededWhenTheStartupFlagIsMissingOrAmbiguous(int flagged)
    {
        var gate = QuestStartupBindingTests.Gate();
        var stages = Enumerable.Range(0, 3).Select(index => new QuestStartupStage(10 * (index + 1), index < flagged, 0)).ToArray();
        var session = Session(gate, Scalar(stages));
        Start(session);
        Assert.Equal(0, session.SeededStages);
        Assert.Empty(session.StartupStages!.Stages);
        Assert.Equal(1, session.StartupStages.Unresolved);
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(Quest));
        Assert.Equal(0, session.QuestStages.StageOf(Quest));
    }

    [Fact]
    public void AStartedQuestWithNoStageDataIsRunningInTheMachineAndAdmittedByDialogue()
    {
        var session = Session(QuestStartupBindingTests.Gate(), QuestStartupBindingTests.Input());
        Start(session);
        Assert.Null(session.StartupStages);
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(Quest));
        Assert.True(DialogueRules.QuestAllows(SessionQuestState.From(session), QuestKey));
    }

    [Fact]
    public void AQuestThatDoesNotStartWithTheGameStaysNotStartedUntilAScriptStartsIt()
    {
        var session = Session(QuestStartupBindingTests.Gate(), QuestStartupBindingTests.Input() with { StartGameEnabled = false });
        Start(session);
        var dialogue = SessionQuestState.From(session);
        Assert.False(DialogueRules.QuestAllows(dialogue, QuestKey));
        Assert.Equal(0.0, DialogueValue(dialogue, ConditionFunctions.GetQuestRunning, 0));
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.Start));
        Assert.True(DialogueRules.QuestAllows(dialogue, QuestKey));
        Assert.Equal(1.0, DialogueValue(dialogue, ConditionFunctions.GetQuestRunning, 0));
    }

    [Fact]
    public void DialogueSeesTheStartStageAndEverySetStageAndCompleteFromTheSessionMachine()
    {
        var session = Session(QuestStartupBindingTests.Gate(), Scalar(new QuestStartupStage(10, true, 0)));
        Start(session);
        var dialogue = SessionQuestState.From(session);
        Assert.Equal(QuestKey.ToString(), Quest);
        Assert.Equal(10.0, DialogueValue(dialogue, ConditionFunctions.GetStage, 0));
        Assert.Equal(1.0, DialogueValue(dialogue, ConditionFunctions.GetStageDone, 10));
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.SetStage, 20));
        Assert.Equal(20.0, DialogueValue(dialogue, ConditionFunctions.GetStage, 0));
        Assert.Equal(1.0, DialogueValue(dialogue, ConditionFunctions.GetStageDone, 10));
        Assert.Equal(1.0, DialogueValue(dialogue, ConditionFunctions.GetStageDone, 20));
        Assert.Equal(0.0, DialogueValue(dialogue, ConditionFunctions.GetStageDone, 15));
        Assert.True(DialogueRules.QuestAllows(dialogue, QuestKey));
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.CompleteQuest));
        Assert.False(DialogueRules.QuestAllows(dialogue, QuestKey));
        Assert.Equal(1.0, DialogueValue(dialogue, ConditionFunctions.GetQuestCompleted, 0));
        Assert.Equal(20.0, DialogueValue(dialogue, ConditionFunctions.GetStage, 0));
    }

    [Fact]
    public void ARestoredQuestSnapshotShowsInDialogueWithoutRebuildingTheDialogueState()
    {
        var session = Session(QuestStartupBindingTests.Gate(), Scalar());
        Start(session);
        var dialogue = SessionQuestState.From(session);
        Assert.Equal(0.0, DialogueValue(dialogue, ConditionFunctions.GetStage, 0));
        session.QuestStages.Restore(new QuestStateSnapshot(new[] { new QuestStateEntry(Quest, QuestState.Completed, 200, new[] { 100, 200 }) }));
        Assert.Equal(200.0, DialogueValue(dialogue, ConditionFunctions.GetStage, 0));
        Assert.Equal(1.0, DialogueValue(dialogue, ConditionFunctions.GetStageDone, 100));
        Assert.Equal(1.0, DialogueValue(dialogue, ConditionFunctions.GetQuestCompleted, 0));
        Assert.False(DialogueRules.QuestAllows(dialogue, QuestKey));
        session.QuestStages.Restore(QuestStateSnapshot.Empty);
        Assert.Equal(0.0, DialogueValue(dialogue, ConditionFunctions.GetStage, 0));
        Assert.Equal(0.0, DialogueValue(dialogue, ConditionFunctions.GetStageDone, 100));
        Assert.False(DialogueRules.QuestAllows(dialogue, QuestKey));
    }

    [Fact]
    public void ARefusedQuestStartupStillLeavesTheStartGameQuestsRunningAndAdmittedByDialogue()
    {
        var good = QuestStartupBindingTests.Input();
        var unsupported = good with { Quest = "000900:QuestProbe.esp", Unsupported = new[] { "synthetic unsupported entry" } };
        var session = Session(QuestStartupBindingTests.Gate(), good, unsupported);
        var result = session.StartNewGame();
        Assert.True(result.Applied);
        Assert.Equal(new[] { Quest }, session.Quests.Started);
        Assert.Equal(unsupported.Quest, Assert.Single(result.Blocked).Quest);
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(Quest));
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(unsupported.Quest));
        Assert.True(DialogueRules.QuestAllows(SessionQuestState.From(session), QuestKey));
        Assert.Same(result, session.StartNewGame());
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(Quest));

        var alone = Session(QuestStartupBindingTests.Gate(), unsupported with { Quest = Quest });
        Assert.False(alone.StartNewGame().Applied);
        Assert.Empty(alone.Quests.Started);
        Assert.Equal(QuestState.Running, alone.QuestStages.StateOf(Quest));
        Assert.True(DialogueRules.QuestAllows(SessionQuestState.From(alone), QuestKey));
    }

    [Fact]
    public void ARestoreWithNoEntryForAStartGameQuestLeavesItRunning()
    {
        var session = Session(QuestStartupBindingTests.Gate(), Scalar());
        Start(session);
        var other = new QuestStateEntry("000999:QuestProbe.esp", QuestState.Running, null, Array.Empty<int>());
        SessionRestore.Apply(session, null, null, new QuestStateSnapshot(new[] { other }));
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(Quest));
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(other.Quest));
        Assert.True(DialogueRules.QuestAllows(SessionQuestState.From(session), QuestKey));
    }

    [Theory]
    [InlineData(QuestState.Stopped)]
    [InlineData(QuestState.Completed)]
    [InlineData(QuestState.NotStarted)]
    public void ARestoreKeepsTheStateRecordedForAStartGameQuest(QuestState recorded)
    {
        var session = Session(QuestStartupBindingTests.Gate(), Scalar());
        Start(session);
        var entry = new QuestStateEntry(Quest, recorded, 30, new[] { 30 });
        SessionRestore.Apply(session, null, null, new QuestStateSnapshot(new[] { entry }));
        Assert.Equal(recorded, session.QuestStages.StateOf(Quest));
        Assert.Equal(30, session.QuestStages.StageOf(Quest));
    }

    private static readonly FormKey QuestKey = new("QuestProbe.esp", 0x800);

    private static double? DialogueValue(IQuestState state, ushort function, int stage)
    {
        var input = DialogueRules.QuestInput(new ConditionRule(0, 1, function, 0, 0), QuestKey, stage, null, state, _ => null);
        return input.Known ? input.Value : null;
    }

    [Fact]
    public void ARealCacheLoadedWithTheEmbeddedRegistryStartsThroughTheLifecycleAndSeedsItsStage()
    {
        using var cache = new TempCache();
        var script = CacheFixture.Candidates(TestHosts.Stub("ScriptObject"), TestHosts.Stub("QuestScript", null, "Int Property Count Auto",
            "Float Property SignedZero Auto", "Bool Property Flag Auto", "String Property Text Auto",
            "Function OnInit()\nEndFunction"));
        var scriptless = Scalar();
        CacheFixture.Build(cache.Root, script, new[]
        {
            Scalar(new QuestStartupStage(5, false, 0), new QuestStartupStage(15, true, 0)),
            scriptless with
            {
                Quest = "000900:QuestProbe.esp",
                Scripts = Array.Empty<string>(),
                Metadata = scriptless.Metadata! with { ScriptProperties = Array.Empty<QuestStartupProperty>() },
            },
        });
        using var session = GameSession.Load(cache.Root);
        session.BindQuestNatives(new[] { Quest });
        var result = session.StartNewGame();
        Assert.True(result.Applied, string.Join("; ", result.Reasons));
        Assert.Equal(15, session.QuestStages.StageOf(Quest));
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(Quest));
        Assert.Equal(1, session.SeededStages);
        Assert.Equal(1, NativeInstances(session));
    }
}
