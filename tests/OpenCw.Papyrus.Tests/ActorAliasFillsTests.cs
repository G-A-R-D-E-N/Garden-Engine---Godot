using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Conditions;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ActorAliasFillsTests
{
    private const string Quest = "000800:Quests.esp";
    private const string Base = "000800:Npcs.esp";

    private static readonly (string Key, string Status)[] Rows =
    {
        (ConditionEvaluator.OperatorsKey, "ENGINE_POLICY"),
        (ConditionEvaluator.FunctionKeyPrefix + "GetQuestVariable", "PROVEN"),
        (ConditionEvaluator.FunctionKeyPrefix + "GetStageDone", "UNPROVEN"),
        (ActorAliasFills.ActorsKey, "UNPROVEN"),
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

    private static ActorAliasFills Fills(EvidenceGate gate)
    {
        var evaluator = new ConditionEvaluator(gate);
        evaluator.Register("GetQuestVariable", condition => condition.Parameter);
        return new ActorAliasFills(evaluator, gate);
    }

    private static ActorAliasRequest Request(int aliasId, AliasFillKind kind, params ConditionInput[] conditions) =>
        new(Quest, aliasId, kind, Base, conditions);

    private static ActorCandidate Candidate(string reference, string baseForm, double parameter) =>
        new(PapyrusOwnerId.ForPlacedReference(baseForm, reference), baseForm,
            new[] { new ConditionInput("GetQuestVariable", ">=", 10, false, parameter) });

    [Fact]
    public void ADecidedFindMatchingConditionPicksTheMatchingCandidate()
    {
        var gate = Gate((ActorAliasFills.ActorsKey, "PROVEN"));
        var report = Fills(gate).Fill(
            new[] { Request(1, AliasFillKind.FindMatching) },
            new[] { Candidate("000A00", Base, 12), Candidate("000A01", Base, 4) });

        var alias = Assert.Single(report.Aliases);
        Assert.Equal(ActorAliasState.Filled, alias.State);
        Assert.True(alias.IsBound);
        Assert.Equal(PapyrusOwnerId.ForPlacedReference(Base, "000A00"), alias.Target);
        Assert.Equal(1, report.Filled);
    }

    [Fact]
    public void AnIndeterminateConditionLeavesTheAliasUnbound()
    {
        var gate = Gate((ActorAliasFills.ActorsKey, "PROVEN"));
        var conditions = new[] { new ConditionInput("GetStageDone", "==", 1, false, 1) };

        var report = Fills(gate).Fill(
            new[] { Request(1, AliasFillKind.FindMatching, conditions) },
            new[] { Candidate("000A00", Base, 12) });

        var alias = Assert.Single(report.Aliases);
        Assert.Equal(ActorAliasState.Unfilled, alias.State);
        Assert.Null(alias.Target);
        Assert.False(alias.IsBound);
        Assert.Contains("indeterminate", alias.Reason, StringComparison.Ordinal);
        Assert.Contains("GetStageDone", alias.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("None", report.ToReport(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoMatchingCandidateLeavesTheAliasUnbound()
    {
        var gate = Gate((ActorAliasFills.ActorsKey, "PROVEN"));
        var conditions = new[] { new ConditionInput("GetQuestVariable", ">=", 100, false, 1) };

        var report = Fills(gate).Fill(
            new[] { Request(1, AliasFillKind.FindMatching, conditions) },
            new[] { Candidate("000A00", Base, 12) });

        Assert.Equal(ActorAliasState.Unfilled, Assert.Single(report.Aliases).State);
        Assert.Equal(1, report.Unfilled);
    }

    [Fact]
    public void AUniqueActorFillsWhenExactlyOneMatches()
    {
        var gate = Gate((ActorAliasFills.ActorsKey, "PROVEN"));
        var report = Fills(gate).Fill(
            new[] { Request(2, AliasFillKind.UniqueActor) },
            new[]
            {
                Candidate("000A00", Base, 1),
                Candidate("000A01", "000900:Other.esp", 1),
            });

        var alias = Assert.Single(report.Aliases);
        Assert.Equal(ActorAliasState.Filled, alias.State);
        Assert.Equal(PapyrusOwnerId.ForPlacedReference(Base, "000A00"), alias.Target);
    }

    [Fact]
    public void AUniqueActorWithNoneOrSeveralStaysUnbound()
    {
        var gate = Gate((ActorAliasFills.ActorsKey, "PROVEN"));
        var fills = Fills(gate);

        var none = fills.Fill(new[] { Request(2, AliasFillKind.UniqueActor) },
            new[] { Candidate("000A00", "000900:Other.esp", 1) });
        Assert.Equal(ActorAliasState.Unfilled, Assert.Single(none.Aliases).State);

        var several = fills.Fill(new[] { Request(3, AliasFillKind.UniqueActor) },
            new[] { Candidate("000A00", Base, 1), Candidate("000A01", Base, 1) });
        Assert.Equal(ActorAliasState.Unfilled, Assert.Single(several.Aliases).State);
        Assert.Contains("rather than guessing", Assert.Single(several.Aliases).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnprovenActorFillKindFillsNothing()
    {
        var gate = Gate();
        var report = Fills(gate).Fill(
            new[]
            {
                Request(1, AliasFillKind.FindMatching),
                Request(2, AliasFillKind.UniqueActor),
            },
            new[] { Candidate("000A00", Base, 12) });

        Assert.Equal(2, report.Open);
        Assert.Equal(0, report.Filled);
        Assert.All(report.Aliases, alias => Assert.False(alias.IsBound));
        Assert.Equal(new[] { "FindMatching", "UniqueActor" }, report.OpenKinds);
    }

    [Fact]
    public void TheReportIsDeterministicAndCountsEveryAlias()
    {
        var gate = Gate((ActorAliasFills.ActorsKey, "PROVEN"));
        var fills = Fills(gate);
        var aliases = new List<ActorAliasRequest>
        {
            Request(2, AliasFillKind.UniqueActor),
            Request(1, AliasFillKind.FindMatching),
        };
        var pool = new[] { Candidate("000A00", Base, 12) };

        var first = fills.Fill(aliases, pool);
        var second = fills.Fill(aliases, pool);

        Assert.Equal(first.Aliases.Count, first.Filled + first.Unfilled + first.Open);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(64, first.Digest().Length);
        Assert.Equal(2, first.Filled);
    }
}
