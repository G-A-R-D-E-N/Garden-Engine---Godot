using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestStartupOwnershipTests : HostFixture
{
    private const string Healthy = "Scriptname QuestScript\nFunction OnInit()\nEndFunction\n";

    private static EvidenceGate Gate(bool ordering) =>
        TestHosts.Gate((QuestRuntime.StartupOrderingKey, ordering ? "ENGINE_POLICY" : "UNPROVEN"));

    private (QuestRuntime Runtime, IScriptHost Host) Setup(HostKind kind, bool ordering = true, bool failing = false)
    {
        var gate = Gate(ordering);
        var host = Track(TestHosts.Create(kind, gate, new ScriptSpec("QuestScript",
            failing ? TestHosts.FailingOnInitSource("QuestScript") : Healthy)));
        if (failing)
        {
            TestHosts.RegisterBoom(host, "QuestScript");
        }
        return (new QuestRuntime(gate), host);
    }

    private static QuestStartupInput Input() => new("000800:QuestProbe.esp", true, new[] { "QuestScript" }, 0, 0);

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void StartupInitializesTheAttachedQuestInstance(HostKind kind)
    {
        var (runtime, host) = Setup(kind);
        Assert.True(runtime.StartNewGame(new[] { Input() }, host).Applied);
        Assert.Equal(ScriptLifecycle.Ready, Assert.Single(host.Instances).Lifecycle);
        Assert.Same(host.Instances[0], Assert.Single(runtime.InstancesFor(Input().Quest)));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void FailedInitializationDoesNotReportAStartedQuestOrRetrySuccess(HostKind kind)
    {
        var (runtime, host) = Setup(kind, failing: true);
        var first = runtime.StartNewGame(new[] { Input() }, host);
        Assert.False(first.Applied);
        Assert.Equal(0, first.Started);
        Assert.Equal(1, first.AttachedScripts);
        Assert.Equal(0, runtime.StartedQuests);
        Assert.Empty(host.Instances);
        Assert.False(runtime.StartNewGame(new[] { Input() }, host).Applied);
        Assert.Empty(host.Instances);
    }

    [Fact]
    public void ARolledBackDirectAttachmentLeavesNoInstanceForASaveToCapture()
    {
        var (runtime, host) = Setup(HostKind.Radicle, failing: true);

        Assert.False(runtime.StartNewGame(new[] { Input() }, host).Applied);

        Assert.Empty(host.Instances);
        Assert.Empty(PapyrusStateSave.Sections(host));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void RepeatingStartupDoesNotAttachAnotherInstanceOrRecountStarts(HostKind kind)
    {
        var (runtime, host) = Setup(kind);
        runtime.StartNewGame(new[] { Input() }, host);
        var second = runtime.StartNewGame(new[] { Input() }, host);
        Assert.True(second.Applied);
        Assert.Equal(0, second.Started);
        Assert.Equal(0, second.AttachedScripts);
        Assert.Single(host.Instances);
        Assert.Equal(1, runtime.StartedQuests);
        Assert.Equal(1, runtime.AttachedScripts);
    }


    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void DuplicateQuestInputsBlockBeforeAnyAttachment(HostKind kind)
    {
        var (runtime, host) = Setup(kind);
        var result = runtime.StartNewGame(new[] { Input(), Input() }, host);
        Assert.False(result.Applied);
        Assert.Empty(host.Instances);
        Assert.Equal(0, runtime.StartedQuests);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void InitializationFailureRollsBackOnlyTheFailingQuests(HostKind kind)
    {
        var (runtime, host) = Setup(kind, failing: true);
        var result = runtime.StartNewGame(new[]
        {
            new QuestStartupInput("000600:QuestProbe.esp", false, Array.Empty<string>(), 0, 0),
            new QuestStartupInput("000700:QuestProbe.esp", true, Array.Empty<string>(), 0, 0),
            Input(),
            new QuestStartupInput("000900:QuestProbe.esp", true, new[] { "QuestScript" }, 0, 0),
        }, host);
        Assert.True(result.Applied);
        Assert.Equal(1, result.Started);
        Assert.Equal(2, result.AttachedScripts);
        Assert.Equal(new[] { "000700:QuestProbe.esp" }, runtime.Started);
        Assert.Equal(1, runtime.StartedQuests);
        Assert.Empty(runtime.InstancesFor("000700:QuestProbe.esp"));
        Assert.Empty(runtime.InstancesFor("000800:QuestProbe.esp"));
        Assert.Empty(runtime.InstancesFor("000900:QuestProbe.esp"));
        Assert.Empty(host.Instances);
        Assert.Equal(0, runtime.AttachedScripts);
        Assert.Equal(2, runtime.BlockedQuests);
        Assert.Equal(new[] { "000800:QuestProbe.esp", "000900:QuestProbe.esp" }, result.Blocked.Select(outcome => outcome.Quest));
    }

    public static IEnumerable<object[]> PreAttachmentCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            foreach (var missingScript in new[] { false, true })
            {
                yield return new object[] { host[0], missingScript };
            }
        }
    }

    [Theory]
    [MemberData(nameof(PreAttachmentCases))]
    public void PreAttachmentFailureStillCountsMixedSkippedDecisions(HostKind kind, bool missingScript)
    {
        var (runtime, host) = Setup(kind);
        var blocked = missingScript ? Input() with { Scripts = new[] { "Missing" } }
            : Input() with { Unsupported = new[] { "project-authored decode refusal" } };
        var result = runtime.StartNewGame(new[]
        {
            new QuestStartupInput("000600:QuestProbe.esp", false, Array.Empty<string>(), 0, 0), blocked,
        }, host);
        Assert.False(result.Applied);
        Assert.Empty(host.Instances);
        Assert.Equal(1, runtime.BlockedQuests);
    }

}
