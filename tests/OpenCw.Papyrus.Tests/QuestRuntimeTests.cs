using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class QuestRuntimeTests
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

    private static ScriptHost Host(EvidenceGate gate, params string[] scripts)
    {
        var library = new ScriptLibrary();
        foreach (var script in scripts)
        {
            library.Add(new PapyrusScript(script, new[]
            {
                new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            }, Array.Empty<string>()));
        }
        return new ScriptHost(library, gate);
    }

    private static QuestStartupInput Input(string quest, bool enabled, string[] scripts, int fragments = 0, int aliases = 0) =>
        new(quest, enabled, scripts, fragments, aliases);

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

        Assert.Equal(1, plan.Ready);
        Assert.Equal(1, plan.Skipped);
        Assert.Equal(0, plan.Blocked);
        Assert.True(plan.CanApply);
    }

    [Fact]
    public void UnsupportedFragmentBehaviorBlocksBeforeMutation()
    {
        var gate = Gate();
        var runtime = new QuestRuntime(gate);
        var host = Host(gate, "QuestScript");
        var plan = runtime.Preflight(new[] { Input("000800:Quests.esp", true, Array.Empty<string>(), fragments: 3) });

        Assert.Equal(1, plan.Blocked);
        Assert.False(plan.CanApply);

        var result = runtime.Apply(plan, host);
        Assert.False(result.Applied);
        Assert.Equal(0, result.AttachedScripts);
        Assert.Equal(0, host.Instances.Count);
    }

    [Fact]
    public void UnsupportedAliasBehaviorBlocks()
    {
        var gate = Gate();
        var runtime = new QuestRuntime(gate);
        var plan = runtime.Preflight(new[] { Input("000800:Quests.esp", true, Array.Empty<string>(), aliases: 2) });

        Assert.Equal(1, plan.Blocked);
        Assert.Contains("aliases", string.Join(" ", plan.Reasons), StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedStartupOrderingBlocks()
    {
        var gate = Gate();
        var runtime = new QuestRuntime(gate);
        var plan = runtime.Preflight(new[] { Input("000800:Quests.esp", true, new[] { "QuestScript" }) });

        Assert.Equal(1, plan.Blocked);
        Assert.Contains("startup ordering", string.Join(" ", plan.Reasons), StringComparison.Ordinal);
    }

    [Fact]
    public void StartupIsTransactional()
    {
        var gate = Gate();
        var runtime = new QuestRuntime(gate);
        var host = Host(gate, "QuestScript");
        var plan = runtime.Preflight(new[]
        {
            Input("000800:Quests.esp", true, Array.Empty<string>()),
            Input("000900:Quests.esp", true, Array.Empty<string>(), fragments: 1),
        });

        Assert.Equal(1, plan.Ready);
        Assert.Equal(1, plan.Blocked);

        var result = runtime.Apply(plan, host);

        Assert.False(result.Applied);
        Assert.Equal(0, host.Instances.Count);
        Assert.Equal(0, runtime.StartedQuests);
        Assert.Equal(1, runtime.BlockedQuests);
        Assert.Equal(1, runtime.Approximations);
    }

    [Fact]
    public void AProvenChainStartsAndSharesTheHost()
    {
        var gate = Gate(
            (QuestRuntime.StartupOrderingKey, "PROVEN"),
            (QuestRuntime.FragmentTimingKey, "PROVEN"),
            (QuestRuntime.AliasFillKey, "PROVEN"));
        var runtime = new QuestRuntime(gate);
        var host = Host(gate, "QuestScript", "AliasScript");
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
            Input("000A00:Quests.esp", true, Array.Empty<string>(), fragments: 1),
            Input("000800:Quests.esp", false, Array.Empty<string>()),
            Input("000900:Quests.esp", true, Array.Empty<string>()),
        };

        var first = runtime.Preflight(inputs).ToReport();
        var shuffled = runtime.Preflight(inputs.AsEnumerable().Reverse()).ToReport();

        Assert.Equal(first, shuffled);
        Assert.Contains("blocked=1", first, StringComparison.Ordinal);
        Assert.Contains("skipped=1", first, StringComparison.Ordinal);
        Assert.Contains("ready=1", first, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("APPROXIMATED", false)]
    [InlineData("APPROXIMATED", true)]
    [InlineData("UNPROVEN", false)]
    [InlineData("UNPROVEN", true)]
    [InlineData("PROVEN", false)]
    [InlineData("ENGINE_POLICY", false)]
    public void OneStartupAttemptCountsOneOrderingDecision(string status, bool suppliedPlan)
    {
        var gate = Gate((QuestRuntime.StartupOrderingKey, status));
        var runtime = new QuestRuntime(gate);
        var host = Host(gate, "QuestScript");
        var input = Input("000800:Quests.esp", true, new[] { "QuestScript" });
        var plan = new QuestStartupPlan(new[] { new QuestStartupOutcome(input.Quest, QuestStartupState.Ready, null) },
            new[] { input }, 1, 0, 0, Array.Empty<string>());
        var result = suppliedPlan ? runtime.Apply(plan, host) : runtime.StartNewGame(new[] { input }, host);

        Assert.Equal(status != "UNPROVEN", result.Applied);
        Assert.Equal(status == "UNPROVEN" ? 0 : 1, host.Instances.Count);
        Assert.Equal(status == "UNPROVEN" ? 0 : 1, runtime.StartedQuests);
        Assert.Equal(status == "UNPROVEN" ? 1 : 0, runtime.BlockedQuests);
        Assert.Equal(status == "APPROXIMATED" ? 1 : 0, gate.ApproximationCount);
        Assert.Equal(status == "UNPROVEN" ? 1 : 0, gate.StopCount);
        if (status == "APPROXIMATED") Assert.Equal(1, gate.Approximations[QuestRuntime.StartupOrderingKey]);
        if (status == "UNPROVEN") Assert.Equal(1, gate.Stops[QuestRuntime.StartupOrderingKey]);
    }
}
