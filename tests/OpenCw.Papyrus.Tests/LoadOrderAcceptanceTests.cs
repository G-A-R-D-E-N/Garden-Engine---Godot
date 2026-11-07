using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Acceptance;
using OpenCommonwealth.Services.Papyrus.Quest;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class LoadOrderAcceptanceTests
{
    private static AcceptanceScript Script(string name, string? parent, int instructions = 1) =>
        new(name, parent, instructions);

    private static QuestStartupInput Startup(string quest, string plugin = "", params string[] scripts) =>
        new(quest, true, scripts, 0, 0, plugin);

    private static IEnumerable<AcceptanceScript[]> Permutations(IReadOnlyList<AcceptanceScript> scripts)
    {
        if (scripts.Count == 0)
        {
            yield return Array.Empty<AcceptanceScript>();
            yield break;
        }
        for (var index = 0; index < scripts.Count; ++index)
        {
            var rest = scripts.Where((_, position) => position != index).ToList();
            foreach (var tail in Permutations(rest))
            {
                yield return new[] { scripts[index] }.Concat(tail).ToArray();
            }
        }
    }

    [Fact]
    public void ResolvesAChainThatEndsAtAnEngineSeed()
    {
        var report = LoadOrderAcceptance.Run(
            new[] { Script("Child", "Middle"), Script("Middle", "ObjectReference") },
            new[] { "ObjectReference" },
            Array.Empty<QuestStartupInput>());

        Assert.True(report.Clean);
        Assert.Equal(2, report.Resolved);
        Assert.Equal(1, report.SeedTypes);
        Assert.Equal(2, report.Chains.Count);
        Assert.Equal(1, report.Chains.Single(chain => chain.Script == "Child").Depth);
    }

    [Fact]
    public void AMissingParentIsReportedNotHidden()
    {
        var report = LoadOrderAcceptance.Run(
            new[] { Script("Orphan", "NotPresent") },
            new[] { "ObjectReference" },
            Array.Empty<QuestStartupInput>());

        Assert.False(report.Clean);
        Assert.Equal(1, report.MissingParent);
        Assert.Single(report.Failures);
        Assert.Contains("NotPresent", report.Failures[0], StringComparison.Ordinal);
        Assert.Contains("missing-parent=1", report.ToReport(), StringComparison.Ordinal);
    }

    [Fact]
    public void ACycleIsReported()
    {
        var report = LoadOrderAcceptance.Run(
            new[] { Script("A", "B"), Script("B", "A") },
            Array.Empty<string>(),
            Array.Empty<QuestStartupInput>());

        Assert.Equal(2, report.Cycle);
        Assert.Equal(2, report.Failures.Count);
        Assert.All(report.Chains, chain => Assert.Equal(ChainState.Cycle, chain.State));
    }

    [Fact]
    public void ADepthLimitIsReported()
    {
        var scripts = Enumerable.Range(0, 8)
            .Select(index => Script($"S{index}", index == 7 ? null : $"S{index + 1}"))
            .ToArray();

        var report = LoadOrderAcceptance.Run(scripts, Array.Empty<string>(), Array.Empty<QuestStartupInput>(), depthLimit: 3);

        Assert.Equal(4, report.DepthExceeded);
        Assert.Contains("exceeded 3 parents", string.Join(" ", report.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void AStartupPluginIsCheckedAgainstTheLoadOrderNotAgainstScriptNames()
    {
        var report = LoadOrderAcceptance.Run(
            new[] { Script("QuestScript", "ObjectReference") },
            new[] { "ObjectReference" },
            new[] { Startup("000800:Quests.esp", "Quests.esp"), Startup("000900:Other.esp", "Other.esp") },
            loadOrder: new[] { "Fallout4.esm", "Quests.esp" });

        Assert.Equal(2, report.StartupEntries);
        Assert.Equal(1, report.UnknownStartupPlugins);
        Assert.Equal(0, report.MissingStartupScripts);
        Assert.Single(report.Failures);
        Assert.Contains("Other.esp", report.Failures[0], StringComparison.Ordinal);
        Assert.Contains("unknown-plugins=1", report.ToReport(), StringComparison.Ordinal);
    }

    [Fact]
    public void AStartupQuestsAttachedScriptsAreCheckedAgainstTheEffectiveSet()
    {
        var report = LoadOrderAcceptance.Run(
            new[] { Script("QuestScript", "ObjectReference") },
            new[] { "ObjectReference" },
            new[]
            {
                Startup("MQ101", "Fallout4.esm", "QuestScript"),
                Startup("MQ102", "Fallout4.esm", "AbsentScript"),
            },
            loadOrder: new[] { "Fallout4.esm" });

        Assert.Equal(0, report.UnknownStartupPlugins);
        Assert.Equal(1, report.MissingStartupScripts);
        Assert.Single(report.Failures);
        Assert.Contains("AbsentScript", report.Failures[0], StringComparison.Ordinal);
        Assert.Contains("missing-startup-scripts=1", report.ToReport(), StringComparison.Ordinal);
    }

    [Fact]
    public void AStartupEntryWhoseOwnerAndScriptsBothResolveIsClean()
    {
        var report = LoadOrderAcceptance.Run(
            new[] { Script("QuestScript", "ObjectReference") },
            new[] { "ObjectReference" },
            new[] { Startup("MQ101", "Fallout4.esm", "QuestScript") },
            loadOrder: new[] { "Fallout4.esm" });

        Assert.True(report.Clean);
        Assert.Equal(1, report.StartupEntries);
        Assert.Equal(0, report.UnknownStartupPlugins);
        Assert.Equal(0, report.MissingStartupScripts);
    }

    [Fact]
    public void APluginNameThatEqualsAScriptNameIsNotAPluginMatch()
    {
        var report = LoadOrderAcceptance.Run(
            new[] { Script("Quests.esp", "ObjectReference") },
            new[] { "ObjectReference" },
            new[] { Startup("000800:Quests.esp", "Quests.esp") },
            loadOrder: new[] { "Fallout4.esm" });

        Assert.Equal(1, report.UnknownStartupPlugins);
        Assert.Equal(0, report.MissingStartupScripts);
    }

    [Fact]
    public void ASeqFileNameWithoutThePluginExtensionStillMatchesTheLoadOrder()
    {
        var report = LoadOrderAcceptance.Run(
            new[] { Script("QuestScript", "ObjectReference") },
            new[] { "ObjectReference" },
            new[] { Startup("MQ101", "FW_Locksmith", "QuestScript") },
            loadOrder: new[] { "Fallout4.esm", "FW_Locksmith.esp" });

        Assert.True(report.Clean);
        Assert.Equal(0, report.UnknownStartupPlugins);
    }

    [Fact]
    public void ThePluginIdentityDropsAPluginExtension()
    {
        Assert.Equal("FW_Locksmith", LoadOrderAcceptance.PluginIdentity("FW_Locksmith.esp"));
        Assert.Equal("FW_Locksmith", LoadOrderAcceptance.PluginIdentity("FW_Locksmith"));
        Assert.Equal("Fallout4", LoadOrderAcceptance.PluginIdentity("Fallout4.esm"));
        Assert.Equal("patch", LoadOrderAcceptance.PluginIdentity("patch.esl"));
        Assert.Equal("no-extension", LoadOrderAcceptance.PluginIdentity("no-extension"));
    }

    [Fact]
    public void WithoutALoadOrderNoPluginIsChecked()
    {
        var report = LoadOrderAcceptance.Run(
            new[] { Script("QuestScript", "ObjectReference") },
            new[] { "ObjectReference" },
            new[] { Startup("MQ101", "Fallout4.esm", "QuestScript") });

        Assert.True(report.Clean);
        Assert.Equal(0, report.UnknownStartupPlugins);
        Assert.Equal(0, report.MissingStartupScripts);
    }

    [Fact]
    public void TheReportIsReproducibleUnderAnyInputOrder()
    {
        var scripts = new List<AcceptanceScript>
        {
            Script("Child", "Middle"),
            Script("Middle", "ObjectReference"),
            Script("Orphan", "NotPresent"),
            Script("LoopA", "LoopB"),
            Script("LoopB", "LoopA"),
        };
        var seeds = new[] { "ObjectReference" };

        var first = LoadOrderAcceptance.Run(scripts, seeds, Array.Empty<QuestStartupInput>());
        foreach (var permutation in Permutations(scripts))
        {
            var other = LoadOrderAcceptance.Run(permutation, seeds, Array.Empty<QuestStartupInput>());
            Assert.Equal(first.ToReport(), other.ToReport());
            Assert.Equal(first.Digest(), other.Digest());
        }
        Assert.Equal(64, first.Digest().Length);
    }

    [Fact]
    public void NoFailureIsHidden()
    {
        var report = LoadOrderAcceptance.Run(
            new[] { Script("Orphan", "NotPresent"), Script("LoopA", "LoopB"), Script("LoopB", "LoopA") },
            Array.Empty<string>(),
            Array.Empty<QuestStartupInput>());

        var failing = report.Chains.Count(chain => chain.State != ChainState.Resolved);
        Assert.Equal(failing, report.Failures.Count);
        Assert.Equal(failing, report.MissingParent + report.Cycle + report.DepthExceeded);
        Assert.False(report.Clean);
    }
}
