using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Conditions;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Story;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class StoryManagerPathsTests
{
    private const string Quest = "000800:Quests.esp";
    private const string Base = "000800:Npcs.esp";

    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.ReflectionKey, "ENGINE_POLICY"),
        (ConditionEvaluator.OperatorsKey, "ENGINE_POLICY"),
        (ConditionEvaluator.FunctionKeyPrefix + "GetQuestVariable", "PROVEN"),
        (ConditionEvaluator.FunctionKeyPrefix + "GetStageDone", "UNPROVEN"),
        (StoryManagerPaths.DispatchKey, "PROVEN"),
        (StoryManagerPaths.FamiliesKey, "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "StoryBeat", "PROVEN"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides)
    {
        var rows = Rows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            rows[pair.Key] = pair.Status;
        }
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static PapyrusOwnerId Owner() => PapyrusOwnerId.ForPlacedReference(Base, "000A00");

    private static (StoryManagerPaths Paths, List<string> Started, PapyrusEventBridge Bridge, List<string> Seen) Setup(
        EvidenceGate gate)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("QuestScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("StoryBeat", new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, gate);
        var seen = new List<string>();
        host.Overrides = new ScriptOverrideRegistry(gate);
        host.Overrides.Register("QuestScript", "StoryBeat", ScriptOverrideRegistry.AnyParameterCount, context =>
        {
            seen.Add(context.Function);
            return OverrideResult.Handled(PapyrusValue.None);
        });
        var bridge = new PapyrusEventBridge(host, gate);
        var instance = host.Attach("QuestScript");
        host.Initialize(instance);
        bridge.Register(Owner(), instance);

        var started = new List<string>();
        var evaluator = new ConditionEvaluator(gate);
        evaluator.Register("GetQuestVariable", condition => condition.Parameter);
        return (new StoryManagerPaths(evaluator, bridge, gate, started.Add), started, bridge, seen);
    }

    private static StoryEvent Event(StoryFamily family, params ConditionInput[] conditions) =>
        new("Node", family, Quest, family == StoryFamily.ScriptEvent ? "StoryBeat" : null, conditions);

    [Fact]
    public void ADecidedQuestStartEventStartsTheQuest()
    {
        var gate = Gate();
        var (paths, started, _, _) = Setup(gate);

        var outcome = paths.Dispatch(Event(StoryFamily.QuestStart), null);

        Assert.Equal(StoryState.Dispatched, outcome.State);
        Assert.Equal(new[] { Quest }, started);
        Assert.Equal(1, paths.Dispatched);
    }

    [Fact]
    public void ADecidedScriptEventIsDeliveredThroughTheBridge()
    {
        var gate = Gate();
        var (paths, _, bridge, seen) = Setup(gate);

        var outcome = paths.Dispatch(Event(StoryFamily.ScriptEvent), Owner());

        Assert.Equal(StoryState.Dispatched, outcome.State);
        Assert.Equal(new[] { "StoryBeat" }, seen);
        Assert.Equal(1, bridge.Delivered);
    }

    [Fact]
    public void AQuestStartWithoutACallbackIsRefusedAndCounted()
    {
        var gate = Gate();
        var (_, _, bridge, _) = Setup(gate);
        var paths = new StoryManagerPaths(new ConditionEvaluator(gate), bridge, gate);

        var outcome = paths.Dispatch(Event(StoryFamily.QuestStart), null);
        var report = paths.Report(new[] { outcome });

        Assert.Equal(StoryState.Refused, outcome.State);
        Assert.Contains("callback", outcome.Reason, StringComparison.Ordinal);
        Assert.Empty(paths.StartedQuests);
        Assert.Equal(0, report.Dispatched);
        Assert.Equal(1, report.Skipped);
        Assert.Equal(0, report.Unsupported);
    }

    [Fact]
    public void AnIndeterminateConditionBlocksTheNode()
    {
        var gate = Gate();
        var (paths, started, _, _) = Setup(gate);
        var conditions = new[] { new ConditionInput("GetStageDone", "==", 1, false, 1) };

        var outcome = paths.Dispatch(Event(StoryFamily.QuestStart, conditions), null);

        Assert.Equal(StoryState.ConditionsIndeterminate, outcome.State);
        Assert.Contains("GetStageDone", outcome.Reason, StringComparison.Ordinal);
        Assert.Empty(started);
        Assert.Equal(1, paths.Skipped);
    }

    [Fact]
    public void AConditionDecidedFalseSkipsTheNode()
    {
        var gate = Gate();
        var (paths, started, _, _) = Setup(gate);
        var conditions = new[] { new ConditionInput("GetQuestVariable", ">=", 100, false, 1) };

        var outcome = paths.Dispatch(Event(StoryFamily.QuestStart, conditions), null);

        Assert.Equal(StoryState.ConditionsFalse, outcome.State);
        Assert.Empty(started);
    }

    [Fact]
    public void ADecidedTrueConditionDispatches()
    {
        var gate = Gate();
        var (paths, started, _, _) = Setup(gate);
        var conditions = new[] { new ConditionInput("GetQuestVariable", ">=", 10, false, 12) };

        var outcome = paths.Dispatch(Event(StoryFamily.QuestStart, conditions), null);

        Assert.Equal(StoryState.Dispatched, outcome.State);
        Assert.Equal(new[] { Quest }, started);
    }

    [Fact]
    public void AnUnsupportedFamilyIsCountedAndNamed()
    {
        var gate = Gate();
        var (paths, _, _, _) = Setup(gate);

        var outcome = paths.Dispatch(Event(StoryFamily.Unsupported), null);

        Assert.Equal(StoryState.FamilyUnsupported, outcome.State);
        Assert.Contains("not implemented", outcome.Reason, StringComparison.Ordinal);
        Assert.Equal(1, paths.Unsupported);
        Assert.Equal(0, paths.Dispatched);
    }

    [Fact]
    public void AnUnprovenDispatchRefusesEverything()
    {
        var gate = Gate((StoryManagerPaths.DispatchKey, "UNPROVEN"));
        var (paths, started, _, _) = Setup(gate);

        var outcome = paths.Dispatch(Event(StoryFamily.QuestStart), null);

        Assert.Equal(StoryState.Refused, outcome.State);
        Assert.Empty(started);
        Assert.Equal(1, paths.Skipped);
    }

    [Fact]
    public void TheReportIsDeterministicAndCountsEveryEvent()
    {
        var gate = Gate();
        var (paths, _, _, _) = Setup(gate);
        var outcomes = new List<StoryOutcome>
        {
            paths.Dispatch(Event(StoryFamily.QuestStart), null),
            paths.Dispatch(Event(StoryFamily.ScriptEvent), Owner()),
            paths.Dispatch(Event(StoryFamily.Unsupported), null),
        };

        var first = paths.Report(outcomes);
        var second = paths.Report(outcomes);

        Assert.Equal(first.Events.Count, first.Dispatched + first.Skipped + first.Unsupported);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(2, first.Dispatched);
        Assert.Equal(1, first.Unsupported);
        Assert.Equal(64, first.Digest().Length);
    }
}
