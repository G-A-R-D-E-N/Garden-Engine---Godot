using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestStartupOwnershipTests
{
    private static (QuestRuntime Runtime, ScriptHost Host) Setup(bool ordering = true, bool failing = false)
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.OnInitBarrier,
            EvidenceKeys.Host, EvidenceKeys.FailClosed };
        var rows = keys.Select(key => $"{{\"key\":\"{key}\",\"status\":\"ENGINE_POLICY\"}}")
            .Append($"{{\"key\":\"{QuestRuntime.StartupOrderingKey}\",\"status\":\"{(ordering ? "ENGINE_POLICY" : "UNPROVEN")}\"}}");
        var gate = new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", rows)}]}}"));
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("QuestScript", new[]
        {
            new PapyrusFunction("OnInit", failing
                ? new[] { PapyrusInstruction.Unsupported("project-authored failure") }
                : new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        return (new QuestRuntime(gate), new ScriptHost(library, gate));
    }

    private static QuestStartupInput Input() => new("000800:QuestProbe.esp", true, new[] { "QuestScript" }, 0, 0);

    [Fact]
    public void StartupInitializesTheAttachedQuestInstance()
    {
        var (runtime, host) = Setup();
        Assert.True(runtime.StartNewGame(new[] { Input() }, host).Applied);
        Assert.Equal(ScriptLifecycle.Ready, Assert.Single(host.Instances).Lifecycle);
        Assert.Same(host.Instances[0], Assert.Single(runtime.InstancesFor(Input().Quest)));
    }

    [Fact]
    public void FailedInitializationDoesNotReportAStartedQuestOrRetrySuccess()
    {
        var (runtime, host) = Setup(failing: true);
        var first = runtime.StartNewGame(new[] { Input() }, host);
        Assert.False(first.Applied);
        Assert.Equal(0, first.Started);
        Assert.Equal(1, first.AttachedScripts);
        Assert.Equal(0, runtime.StartedQuests);
        Assert.Equal(ScriptLifecycle.Failed, Assert.Single(host.Instances).Lifecycle);
        Assert.False(runtime.StartNewGame(new[] { Input() }, host).Applied);
        Assert.Single(host.Instances);
        Assert.Equal(0, runtime.Approximations);
    }

    [Fact]
    public void RepeatingStartupDoesNotAttachAnotherInstanceOrRecountStarts()
    {
        var (runtime, host) = Setup();
        runtime.StartNewGame(new[] { Input() }, host);
        var second = runtime.StartNewGame(new[] { Input() }, host);
        Assert.True(second.Applied);
        Assert.Equal(0, second.Started);
        Assert.Equal(0, second.AttachedScripts);
        Assert.Single(host.Instances);
        Assert.Equal(1, runtime.StartedQuests);
        Assert.Equal(1, runtime.AttachedScripts);
    }

    [Fact]
    public void ACallerCreatedReadyPlanCannotBypassTheCanonicalPreflight()
    {
        var (runtime, host) = Setup(ordering: false);
        var input = Input();
        var forged = new QuestStartupPlan(new[] { new QuestStartupOutcome(input.Quest, QuestStartupState.Ready, null) },
            new[] { input }, 1, 0, 0, Array.Empty<string>());
        Assert.False(runtime.Apply(forged, host).Applied);
        Assert.Empty(host.Instances);
        Assert.Equal(1, runtime.BlockedQuests);
    }

    [Fact]
    public void DuplicateQuestInputsBlockBeforeAnyAttachment()
    {
        var (runtime, host) = Setup();
        var result = runtime.StartNewGame(new[] { Input(), Input() }, host);
        Assert.False(result.Applied);
        Assert.Empty(host.Instances);
        Assert.Equal(0, runtime.StartedQuests);
    }

    [Fact]
    public void InitializationFailureStopsLaterQuestsAndKeepsEarlierStartsAndSkipCounts()
    {
        var (runtime, host) = Setup(failing: true);
        var result = runtime.StartNewGame(new[]
        {
            new QuestStartupInput("000600:QuestProbe.esp", false, Array.Empty<string>(), 0, 0),
            new QuestStartupInput("000700:QuestProbe.esp", true, Array.Empty<string>(), 0, 0),
            Input(),
            new QuestStartupInput("000900:QuestProbe.esp", true, new[] { "QuestScript" }, 0, 0),
        }, host);
        Assert.False(result.Applied);
        Assert.Equal(1, result.Started);
        Assert.Equal(1, result.AttachedScripts);
        Assert.Equal("000700:QuestProbe.esp", Assert.Single(runtime.Started));
        Assert.Single(host.Instances);
        Assert.Empty(runtime.InstancesFor("000900:QuestProbe.esp"));
        Assert.Equal(1, runtime.SkippedQuests);
        Assert.Equal(1, runtime.BlockedQuests);
        Assert.Equal(0, runtime.Approximations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreAttachmentFailureStillCountsMixedSkippedDecisions(bool missingScript)
    {
        var (runtime, host) = Setup();
        var blocked = missingScript ? Input() with { Scripts = new[] { "Missing" } }
            : Input() with { Unsupported = new[] { "project-authored decode refusal" } };
        var result = runtime.StartNewGame(new[]
        {
            new QuestStartupInput("000600:QuestProbe.esp", false, Array.Empty<string>(), 0, 0), blocked,
        }, host);
        Assert.False(result.Applied);
        Assert.Empty(host.Instances);
        Assert.Equal(1, runtime.BlockedQuests);
        Assert.Equal(1, runtime.SkippedQuests);
    }
}
