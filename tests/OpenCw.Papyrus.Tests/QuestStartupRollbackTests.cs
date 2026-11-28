using System;
using System.Linq;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestStartupRollbackTests : HostFixture
{
    private static readonly ScriptSpec[] Scripts =
    {
        TestHosts.Stub("Form"),
        TestHosts.Stub("QuestScript", null,
            "Int Property Count Auto",
            "Float Property SignedZero Auto",
            "Bool Property Flag Auto",
            "String Property Text Auto",
            "Form Property Link Auto",
            "Function OnInit()\nEndFunction",
            "Function OnProbe()\nEndFunction"),
        new("BrokenScript", TestHosts.FailingOnInitSource("BrokenScript")),
    };

    private GameSession Session(EvidenceGate gate, params QuestStartupInput[] startup)
    {
        var host = Track(TestHosts.Create(HostKind.Radicle, gate, Scripts));
        TestHosts.RegisterBoom(host, "BrokenScript");
        return new GameSession(host, gate, startup);
    }

    private static QuestStartupInput DisabledInput(string quest) =>
        QuestStartupBindingTests.Input() with { Quest = quest, StartGameEnabled = false, Scripts = Array.Empty<string>() };

    [Fact]
    public void RetryingABlockedPlanReportsOneAttemptsCounters()
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = Session(gate, QuestStartupBindingTests.Input(), DisabledInput("000900:QuestProbe.esp"));
        session.Host.Overrides = new ScriptOverrideRegistry(gate);
        session.Host.Overrides.Register("QuestScript", "OnInit", 0,
            _ => OverrideResult.Refused("synthetic initialization failure"));

        Assert.False(session.StartNewGame().Applied);
        Assert.Equal(1, session.Quests.BlockedQuests);

        Assert.False(session.StartNewGame().Applied);
        Assert.Equal(1, session.Quests.BlockedQuests);
    }

    [Fact]
    public void AnOnInitThatFailsRollsBackOnlyThatQuest()
    {
        var gate = QuestStartupBindingTests.Gate();
        var first = QuestStartupBindingTests.Input() with { Quest = "000700:QuestProbe.esp" };
        var last = first with { Quest = "000900:QuestProbe.esp" };
        var bad = first with
        {
            Quest = "000800:QuestProbe.esp",
            Scripts = new[] { "BrokenScript" },
            Metadata = new QuestStartupMetadata(Array.Empty<QuestStartupProperty>(), "",
                Array.Empty<QuestStartupProperty>(), Array.Empty<QuestStartupFragment>(), Array.Empty<int>(),
                Array.Empty<QuestStartupAliasScript>(), Array.Empty<QuestStartupProperty>()),
        };
        var session = Session(gate, last, bad, first);

        var result = session.StartNewGame();

        Assert.True(result.Applied);
        Assert.Equal(2, result.Started);
        Assert.Equal(new[] { first.Quest, last.Quest }, session.Quests.Started);
        Assert.Empty(session.Quests.InstancesFor(bad.Quest));
        foreach (var quest in new[] { first.Quest, last.Quest })
        {
            var started = Assert.Single(session.Quests.InstancesFor(quest));
            Assert.Equal(ScriptLifecycle.Ready, started.Lifecycle);
            Assert.False(started.Detached);
            Assert.True(session.Host.RunEvent(started, "OnProbe").Completed);
        }
        Assert.Equal(2, session.Host.Instances.Count);
        Assert.DoesNotContain(session.Host.Instances, instance => instance.Lifecycle == ScriptLifecycle.Failed);
        Assert.Equal(2, session.Quests.AttachedScripts);
        Assert.Equal(1, session.Quests.BlockedQuests);
        var outcome = Assert.Single(result.Blocked);
        Assert.Equal(bad.Quest, outcome.Quest);
        Assert.StartsWith(bad.Quest + ": ", Assert.Single(result.Reasons), StringComparison.Ordinal);
        Assert.Same(result, session.StartupResult);
        Assert.Equal(2, session.Events.RegisteredOwners);
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(bad.Quest));
    }

    [Theory]
    [InlineData(0, "synthetic unsupported entry")]
    [InlineData(1, "missing script Missing")]
    [InlineData(2, "unattached script Elsewhere")]
    public void OneBlockedQuestOfThreeLeavesTheOtherTwoStarted(int cause, string expected)
    {
        var gate = QuestStartupBindingTests.Gate();
        var template = QuestStartupBindingTests.Input();
        var first = template with { Quest = "000700:QuestProbe.esp" };
        var last = template with { Quest = "000900:QuestProbe.esp" };
        var properties = template.Metadata!.ScriptProperties.ToArray();
        properties[0] = properties[0] with { Script = "Elsewhere" };
        var blocked = cause switch
        {
            0 => template with { Unsupported = new[] { "synthetic unsupported entry" } },
            1 => template with { Scripts = new[] { "Missing" } },
            _ => template with { Metadata = template.Metadata with { ScriptProperties = properties } },
        };
        var session = Session(gate, last, blocked, first);

        var result = session.StartNewGame();

        Assert.True(result.Applied);
        Assert.Equal(2, result.Started);
        Assert.Equal(new[] { first.Quest, last.Quest }, session.Quests.Started);
        var outcome = Assert.Single(result.Blocked);
        Assert.Equal(blocked.Quest, outcome.Quest);
        Assert.Equal(QuestStartupState.Blocked, outcome.State);
        Assert.Contains(expected, outcome.Reason!, StringComparison.Ordinal);
        Assert.Equal(blocked.Quest + ": " + outcome.Reason, Assert.Single(result.Reasons));
        Assert.Equal(2, session.Host.Instances.Count);
        Assert.All(session.Host.Instances, instance =>
        {
            Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
            Assert.False(instance.Detached);
        });
        Assert.Equal(1, session.Quests.BlockedQuests);
        Assert.Equal(2, session.Quests.AttachedScripts);
        Assert.Same(result, session.StartupResult);
        Assert.Equal(2, session.Events.RegisteredOwners);
        Assert.Equal(QuestState.Running, session.QuestStages.StateOf(blocked.Quest));
    }

    [Fact]
    public void AnUnsupportedQuestThatIsNotStartGameEnabledIsSkippedWithItsWarning()
    {
        var runtime = new QuestRuntime(QuestStartupBindingTests.Gate());
        var disabled = DisabledInput("000800:QuestProbe.esp") with { Unsupported = new[] { "project-authored decode refusal" } };

        var plan = runtime.Preflight(new[] { disabled });

        Assert.Equal(0, plan.Blocked);
        Assert.Empty(plan.Reasons);
        var outcome = Assert.Single(plan.Outcomes);
        Assert.Equal(QuestStartupState.Skipped, outcome.State);
        Assert.Contains("not start-game enabled", outcome.Reason!, StringComparison.Ordinal);
        Assert.Contains("project-authored decode refusal", outcome.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnsupportedQuestThatIsStartGameEnabledStillBlocks()
    {
        var runtime = new QuestRuntime(QuestStartupBindingTests.Gate());
        var enabled = QuestStartupBindingTests.Input() with { Unsupported = new[] { "project-authored decode refusal" } };

        var plan = runtime.Preflight(new[] { enabled });

        Assert.Equal(1, plan.Blocked);
        Assert.Equal(QuestStartupState.Blocked, Assert.Single(plan.Outcomes).State);
        Assert.Contains("project-authored decode refusal", string.Join(" ", plan.Reasons), StringComparison.Ordinal);
    }
}
