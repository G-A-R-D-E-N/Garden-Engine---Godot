using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Coverage;
using OpenCommonwealth.Services.Papyrus.Events;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class CoverageBaselineTests
{
    [Theory]
    [InlineData("owner.quest", 12)]
    [InlineData("scripts.attached", 55)]
    [InlineData("native.Utility", 7)]
    [InlineData("fragments.stage", 21)]
    public void AMeasuredCountDoesNotProveRuntimeSupport(string id, long count)
    {
        var baseline = CoverageBaselineBuilder.Build(Inputs());

        var row = baseline.Find(id)!;
        Assert.Equal(count, row.Count);
        Assert.Equal(CoverageStatus.Unsupported, row.Status);
        Assert.Contains("measurement", row.Note!, StringComparison.Ordinal);
    }

    private static CoverageInputs Inputs() => new(
        new Dictionary<string, long> { ["quest"] = 12, ["reference"] = 40 },
        55,
        new Dictionary<string, long> { ["Debug.Trace"] = 3, ["Utility"] = 7 },
        new Dictionary<string, long> { ["OnInit"] = 9, ["OnActivate"] = 4 },
        21,
        new[] { "vm.assignment.defaults", "seq.precedence" },
        new[] { "alias fill", "dialogue" });

    private static CoverageInputs InputsWithEvents(params EventSupport[] events) => new(
        new Dictionary<string, long> { ["quest"] = 12, ["reference"] = 40 },
        55,
        new Dictionary<string, long> { ["Debug.Trace"] = 3, ["Utility"] = 7 },
        new Dictionary<string, long> { ["OnInit"] = 9, ["OnActivate"] = 4 },
        21,
        new[] { "vm.assignment.defaults", "seq.precedence" },
        new[] { "alias fill", "dialogue" },
        events);

    [Fact]
    public void CountsEveryMeasuredGroup()
    {
        var baseline = CoverageBaselineBuilder.Build(Inputs());

        Assert.Equal(12, baseline.Find("owner.quest")!.Count);
        Assert.Equal(40, baseline.Find("owner.reference")!.Count);
        Assert.Equal(55, baseline.Find("scripts.attached")!.Count);
        Assert.Equal(7, baseline.Find("native.Utility")!.Count);
        Assert.Equal(9, baseline.Find("event.OnInit")!.Count);
        Assert.Equal(21, baseline.Find("fragments.stage")!.Count);
    }

    [Fact]
    public void UnsupportedKeysBecomeTheirOwnRows()
    {
        var baseline = CoverageBaselineBuilder.Build(Inputs());

        var row = baseline.Find("unsupported.vm.assignment.defaults");
        Assert.NotNull(row);
        Assert.Equal(CoverageStatus.Unsupported, row!.Status);
        Assert.False(string.IsNullOrWhiteSpace(row.Note));
        Assert.Equal(CoverageStatus.Unsupported, baseline.Find("unsupported.seq.precedence")!.Status);
        Assert.Equal(2, baseline.Rows.Count(entry =>
            entry.Status == CoverageStatus.Unsupported
            && entry.Id.StartsWith("unsupported.", StringComparison.Ordinal)));
    }

    [Fact]
    public void MissingSystemsBecomeRowsRatherThanOmissions()
    {
        var baseline = CoverageBaselineBuilder.Build(Inputs());

        Assert.Equal(2, baseline.RowsOf(CoverageStatus.Missing));
        Assert.NotNull(baseline.Find("missing.alias fill"));
        Assert.NotNull(baseline.Find("missing.dialogue"));
    }

    [Fact]
    public void TheBaselineIsReproducible()
    {
        var baseline = CoverageBaselineBuilder.Build(Inputs());
        var other = CoverageBaselineBuilder.Build(Inputs());

        Assert.Equal(baseline.ToReport(), other.ToReport());
        Assert.Equal(baseline.Digest(), other.Digest());
        Assert.Equal(64, baseline.Digest().Length);
    }

    [Fact]
    public void ARowOrderIsStable()
    {
        var baseline = CoverageBaselineBuilder.Build(Inputs());
        var ids = baseline.Rows.Select(row => row.Id).ToArray();

        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal).ToArray(), ids);
        Assert.Contains("event.OnActivate", ids, StringComparer.Ordinal);
    }

    [Fact]
    public void ATaskCanShowExactlyWhatItClosed()
    {
        var baseline = CoverageBaselineBuilder.Build(Inputs());

        var delta = CoverageBaselineBuilder.Close(baseline, "C3", new Dictionary<string, long>
        {
            ["unsupported.vm.assignment.defaults"] = 1,
            ["owner.quest"] = 14,
        });

        Assert.Equal(baseline.Digest(), delta.BaselineDigest);
        Assert.Equal(2, delta.Closures.Count);
        Assert.Equal("owner.quest", delta.Closures[0].RowId);
        Assert.Equal(2, delta.Closures[0].Delta);
        Assert.Equal("unsupported.vm.assignment.defaults", delta.Closures[1].RowId);
        Assert.Equal(1, delta.Closures[1].Delta);
        Assert.Contains("C3", delta.ToReport(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingARowThatIsNotInTheBaselineIsRefused()
    {
        var baseline = CoverageBaselineBuilder.Build(Inputs());

        Assert.Throws<InvalidOperationException>(() =>
            CoverageBaselineBuilder.Close(baseline, "C9", new Dictionary<string, long> { ["owner.actor"] = 1 }));
    }

    [Fact]
    public void ADeclaredHandlerIsCoveredOnlyWhenGardenCanEmitIt()
    {
        var baseline = CoverageBaselineBuilder.Build(InputsWithEvents(
            new EventSupport("OnInit", "event.lifecycle", EventSupportState.Supported, "proven"),
            new EventSupport("OnActivate", null, EventSupportState.NotEmitted, "no fixed emitter")));

        var covered = baseline.Find("event.OnInit")!;
        var counted = baseline.Find("event.OnActivate")!;

        Assert.Equal(CoverageStatus.Covered, covered.Status);
        Assert.Equal(9, covered.Count);
        Assert.Equal(CoverageStatus.Unsupported, counted.Status);
        Assert.Equal(4, counted.Count);
        Assert.Contains("no fixed emitter", counted.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public void AHandlerIsMatchedByItsCompiledNameInAnyCase()
    {
        var inputs = InputsWithEvents(
            new EventSupport("OnLoad", "event.reference.lifecycle", EventSupportState.Supported, "proven"),
            new EventSupport("Quest.OnStageSet", "quest.alias.runtime", EventSupportState.Approximated, "counted")) with
        {
            EventHandlers = new Dictionary<string, long> { ["onLoad"] = 22, ["::remote_Quest_OnStageSet"] = 8 },
        };

        var baseline = CoverageBaselineBuilder.Build(inputs);

        Assert.Equal(CoverageStatus.Covered, baseline.Find("event.onLoad")!.Status);
        Assert.Equal(CoverageStatus.Approximated, baseline.Find("event.::remote_Quest_OnStageSet")!.Status);
    }

    [Fact]
    public void AnApproximatedEventIsCountedAsApproximated()
    {
        var baseline = CoverageBaselineBuilder.Build(InputsWithEvents(
            new EventSupport("OnInit", "event.lifecycle", EventSupportState.Approximated, "counted")));

        Assert.Equal(CoverageStatus.Approximated, baseline.Find("event.OnInit")!.Status);
    }

    [Fact]
    public void WithoutAnEventContractEveryHandlerIsCounted()
    {
        var baseline = CoverageBaselineBuilder.Build(Inputs());

        Assert.Equal(CoverageStatus.Unsupported, baseline.Find("event.OnInit")!.Status);
        Assert.Equal(CoverageStatus.Unsupported, baseline.Find("event.OnActivate")!.Status);
        Assert.Contains("no fixed emitter", baseline.Find("event.OnInit")!.Note!, StringComparison.Ordinal);
    }
}
