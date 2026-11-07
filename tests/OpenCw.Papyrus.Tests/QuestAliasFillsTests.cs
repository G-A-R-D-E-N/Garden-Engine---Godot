using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class QuestAliasFillsTests
{
    private const string Quest = "000800:Quests.esp";

    private static readonly (string Key, string Status)[] Rows =
    {
        (QuestAliasFills.KeyPrefix + "forced_reference", "ENGINE_POLICY"),
        (QuestAliasFills.KeyPrefix + "create_reference", "ENGINE_POLICY"),
        (QuestAliasFills.KeyPrefix + "location", "ENGINE_POLICY"),
        (QuestAliasFills.ActorKey, "UNPROVEN"),
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

    private static QuestAliasInput Alias(int id, AliasFillKind kind, string? target = null, string? location = null) =>
        new(Quest, id, kind, target, location);

    [Fact]
    public void AForcedReferenceFills()
    {
        var report = new QuestAliasFills(Gate()).Fill(new[] { Alias(1, AliasFillKind.ForcedReference, "000A00") });

        var alias = Assert.Single(report.Aliases);
        Assert.Equal(AliasState.Filled, alias.State);
        Assert.Equal("000A00", alias.Value);
        Assert.Equal(1, report.Filled);
        Assert.Equal("alias:000800:Quests.esp#1", alias.Owner.Key);
    }

    [Fact]
    public void CreatedReferencesStayDistinct()
    {
        var fills = new QuestAliasFills(Gate());
        var report = fills.Fill(new[]
        {
            Alias(1, AliasFillKind.CreateReference, "000800:Quests.esp"),
            Alias(2, AliasFillKind.CreateReference, "000800:Quests.esp"),
        });

        Assert.Equal(2, report.Filled);
        Assert.NotEqual(report.Aliases[0].Value, report.Aliases[1].Value);
        Assert.Equal(2, fills.References.CountFor("000800:Quests.esp"));
    }

    [Fact]
    public void ALocationFills()
    {
        var report = new QuestAliasFills(Gate()).Fill(new[] { Alias(3, AliasFillKind.Location, location: "000A10") });

        Assert.Equal("000A10", Assert.Single(report.Aliases).Value);
        Assert.Equal(1, report.Filled);
    }

    [Fact]
    public void FindMatchingStaysOpenForC11b()
    {
        var report = new QuestAliasFills(Gate()).Fill(new[] { Alias(4, AliasFillKind.FindMatching, "ActorType") });

        var alias = Assert.Single(report.Aliases);
        Assert.Equal(AliasState.Open, alias.State);
        Assert.Null(alias.Value);
        Assert.Equal(1, report.Open);
        Assert.Equal(new[] { "FindMatching" }, report.OpenKinds);
    }

    [Fact]
    public void UniqueActorStaysOpenForC11b()
    {
        var report = new QuestAliasFills(Gate()).Fill(new[] { Alias(5, AliasFillKind.UniqueActor, "ActorBase") });

        Assert.Equal(AliasState.Open, Assert.Single(report.Aliases).State);
        Assert.Equal(new[] { "UniqueActor" }, report.OpenKinds);
    }

    [Fact]
    public void AnUnprovenKindLeavesOnlyItsAliasOpen()
    {
        var gate = Gate((QuestAliasFills.KeyPrefix + "location", "UNPROVEN"));
        var report = new QuestAliasFills(gate).Fill(new[]
        {
            Alias(1, AliasFillKind.ForcedReference, "000A00"),
            Alias(2, AliasFillKind.Location, location: "000A10"),
        });

        Assert.Equal(1, report.Filled);
        Assert.Equal(1, report.Open);
        Assert.Equal(AliasState.Filled, report.Aliases[0].State);
        Assert.Equal(AliasState.Open, report.Aliases[1].State);
        Assert.Contains("not proven", report.Aliases[1].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReportIsDeterministicAndCountsEveryAlias()
    {
        var fills = new QuestAliasFills(Gate());
        var aliases = new List<QuestAliasInput>
        {
            Alias(2, AliasFillKind.Location, location: "000A10"),
            Alias(1, AliasFillKind.ForcedReference, "000A00"),
            Alias(3, AliasFillKind.FindMatching, "ActorType"),
        };

        var first = fills.Fill(aliases);
        var second = fills.Fill(aliases);

        Assert.Equal(first.Aliases.Count, first.Filled + first.Open);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(64, first.Digest().Length);
        Assert.Equal(2, first.Filled);
        Assert.Equal(1, first.Open);
    }
}
