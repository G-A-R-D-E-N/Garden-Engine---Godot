using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class QuestRuntimeTests : HostFixture
{
    private static readonly (string Key, string Status)[] PolicyRows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (QuestRuntime.StartupOrderingKey, "UNPROVEN"),
        (QuestRuntime.FragmentTimingKey, "UNPROVEN"),
        (QuestRuntime.AliasFillKey, "UNPROVEN"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides)
    {
        var rows = PolicyRows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            rows[pair.Key] = pair.Status;
        }
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private IScriptHost Host(HostKind kind, EvidenceGate gate, params string[] scripts)
    {
        var host = TestHosts.Create(kind, gate, scripts.Select(script => new ScriptSpec(script,
            $"Scriptname {script}\nFunction OnInit()\nEndFunction\n")).ToArray());
        return Track(host);
    }

    public static IEnumerable<object[]> OrderingCases()
    {
        var rows = new (string Status, bool SuppliedPlan)[]
        {
            ("APPROXIMATED", false),
            ("APPROXIMATED", true),
            ("UNPROVEN", false),
            ("UNPROVEN", true),
            ("PROVEN", false),
            ("ENGINE_POLICY", false),
        };
        foreach (var host in TestHosts.HostKinds)
        {
            foreach (var (status, suppliedPlan) in rows)
            {
                yield return new object[] { host[0], status, suppliedPlan };
            }
        }
    }

    private static QuestStartupInput Input(string quest, bool enabled, string[] scripts, int fragments = 0, int aliases = 0) =>
        new(quest, enabled, scripts, fragments, aliases);

    private static QuestStartupInput Unsupported(string quest) =>
        Input(quest, true, Array.Empty<string>()) with { Unsupported = new[] { "probe" } };

    [Fact]
    public void StartEnabledQuestsPreflightAndStart()
    {
        var gate = Gate();
        var runtime = new QuestRuntime(gate);
        var plan = runtime.Preflight(new[]
        {
            Input("000800:Quests.esp", true, Array.Empty<string>()),
            Input("000900:Quests.esp", false, Array.Empty<string>()),
        });

        Assert.Equal(new[]
        {
            new QuestStartupOutcome("000800:Quests.esp", QuestStartupState.Ready, null),
            new QuestStartupOutcome("000900:Quests.esp", QuestStartupState.Skipped, "not start-game enabled"),
        }, plan.Outcomes);
        Assert.Equal(0, plan.Blocked);
    }





    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AProvenChainStartsAndSharesTheHost(HostKind kind)
    {
        var gate = Gate(
            (QuestRuntime.StartupOrderingKey, "PROVEN"),
            (QuestRuntime.FragmentTimingKey, "PROVEN"),
            (QuestRuntime.AliasFillKey, "PROVEN"));
        var runtime = new QuestRuntime(gate);
        var host = Host(kind, gate, "QuestScript", "AliasScript");
        var result = runtime.StartNewGame(new[]
        {
            Input("000800:Quests.esp", true, new[] { "QuestScript", "AliasScript" }, fragments: 1, aliases: 1),
        }, host);

        Assert.True(result.Applied);
        Assert.Equal(1, result.Started);
        Assert.Equal(2, result.AttachedScripts);
        Assert.Equal(2, host.Instances.Count);
        Assert.Equal(1, runtime.StartedQuests);
        Assert.Equal(2, runtime.AttachedScripts);
        Assert.Equal(0, runtime.BlockedQuests);
    }

    [Fact]
    public void CountersAreExactAndThePlanIsDeterministic()
    {
        var gate = Gate();
        var runtime = new QuestRuntime(gate);
        var inputs = new List<QuestStartupInput>
        {
            Unsupported("000A00:Quests.esp"),
            Input("000800:Quests.esp", false, Array.Empty<string>()),
            Input("000900:Quests.esp", true, Array.Empty<string>()),
        };

        var first = runtime.Preflight(inputs);
        var shuffled = runtime.Preflight(inputs.AsEnumerable().Reverse());

        Assert.Equal(first.Outcomes, shuffled.Outcomes);
        Assert.Equal(new[]
        {
            new QuestStartupOutcome("000800:Quests.esp", QuestStartupState.Skipped, "not start-game enabled"),
            new QuestStartupOutcome("000900:Quests.esp", QuestStartupState.Ready, null),
            new QuestStartupOutcome("000A00:Quests.esp", QuestStartupState.Blocked, "probe"),
        }, first.Outcomes);
        Assert.Equal(1, first.Blocked);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ABlockedQuestDoesNotStopTheReadyQuestFromStarting(HostKind kind)
    {
        var gate = Gate();
        var runtime = new QuestRuntime(gate);
        var host = Host(kind, gate, "QuestScript");
        var plan = runtime.Preflight(new[]
        {
            Input("000800:Quests.esp", true, Array.Empty<string>()),
            Unsupported("000900:Quests.esp"),
        });

        Assert.Equal(new[] { QuestStartupState.Ready, QuestStartupState.Blocked }, plan.Outcomes.Select(outcome => outcome.State));
        Assert.Equal(1, plan.Blocked);

        var result = runtime.Apply(plan, host);

        Assert.True(result.Applied);
        Assert.Equal(1, result.Started);
        Assert.Equal(new[] { "000800:Quests.esp" }, runtime.Started);
        Assert.Equal("000900:Quests.esp", Assert.Single(result.Blocked).Quest);
        Assert.Equal(0, host.Instances.Count);
        Assert.Equal(1, runtime.StartedQuests);
        Assert.Equal(1, runtime.BlockedQuests);
    }

}
