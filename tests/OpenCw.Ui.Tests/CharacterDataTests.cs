using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterDataTests
{
    static readonly OwnedPerkRequirementChecker Checker = new();

    static readonly GenericCharacterData Data = new();

    [Theory]
    [InlineData("own:p1", 1, true)]
    [InlineData("own:p1", 2, true)]
    [InlineData("own:p1", 0, false)]
    [InlineData("own:p1", -1, false)]
    public void Own_requirement_is_met_only_for_a_positive_rank(string requirement, int rank, bool expected)
    {
        var ranks = new Dictionary<string, int> { ["p1"] = rank };

        Assert.Equal(expected, Checker.IsMet(requirement, ranks));
    }

    [Fact]
    public void Own_requirement_is_not_met_when_the_perk_is_missing_or_a_different_perk_is_owned()
    {
        Assert.False(Checker.IsMet("own:p1", new Dictionary<string, int>()));
        Assert.False(Checker.IsMet("own:p1", new Dictionary<string, int> { ["p2"] = 3 }));
        Assert.False(Checker.IsMet("own:", new Dictionary<string, int> { ["p1"] = 1 }));
    }

    [Theory]
    [InlineData("p1")]
    [InlineData("OWN:p1")]
    [InlineData("Own:p1")]
    [InlineData("xown:p1")]
    [InlineData("own p1")]
    [InlineData("")]
    public void Requirement_without_the_exact_own_prefix_is_never_met(string requirement)
    {
        var ranks = new Dictionary<string, int> { ["p1"] = 1, [requirement] = 1 };

        Assert.False(Checker.IsMet(requirement, ranks));
    }

    [Fact]
    public void Own_requirement_matches_the_perk_id_case_sensitively()
    {
        Assert.False(Checker.IsMet("own:P1", new Dictionary<string, int> { ["p1"] = 1 }));
    }

    [Fact]
    public void Generic_data_exposes_seven_stats_with_exact_ids_labels_and_limits()
    {
        Assert.Equal(new[] { "s1", "s2", "s3", "s4", "s5", "s6", "s7" }, Data.Stats.Select(s => s.Id));
        Assert.Equal(new[] { "Stat 1", "Stat 2", "Stat 3", "Stat 4", "Stat 5", "Stat 6", "Stat 7" }, Data.Stats.Select(s => s.Label));
        Assert.All(Data.Stats, s =>
        {
            Assert.Equal(1, s.Min);
            Assert.Equal(10, s.Max);
            Assert.Equal(1, s.PointCost);
            Assert.Equal(string.Empty, s.Description);
        });
        Assert.Equal(5, Data.StatPoints);
        Assert.True(Data.RequireAllStatPointsSpent);
    }

    [Fact]
    public void Generic_data_presets_have_exact_names_and_values()
    {
        Assert.Equal(new[] { "Even", "Focused" }, Data.StatPresets.Select(p => p.Name));

        var even = Data.StatPresets[0].Values;
        Assert.Equal(new[] { "s1", "s2", "s3", "s4", "s5" }, even.Keys.OrderBy(k => k));
        Assert.All(even.Values, v => Assert.Equal(2, v));

        var focused = Data.StatPresets[1].Values;
        Assert.Equal(new[] { "s1" }, focused.Keys);
        Assert.Equal(6, focused["s1"]);
    }

    [Fact]
    public void Generic_data_perks_have_exact_ids_categories_ranks_and_requirements()
    {
        Assert.Equal(new[] { "p1", "p2", "p3" }, Data.Perks.Select(p => p.Id));
        Assert.Equal(new[] { "Perk 1", "Perk 2", "Perk 3" }, Data.Perks.Select(p => p.Name));
        Assert.Equal(new[] { "Group A", "Group A", "Group B" }, Data.Perks.Select(p => p.Category));

        Assert.Equal(new[] { "Rank one of perk 1.", "Rank two of perk 1." }, Data.Perks[0].Ranks.Select(r => r.Text));
        Assert.Equal(new[] { "Perk 2 text." }, Data.Perks[1].Ranks.Select(r => r.Text));
        Assert.Equal(new[] { "Perk 3 text." }, Data.Perks[2].Ranks.Select(r => r.Text));

        Assert.Null(Data.Perks[0].Ranks[0].Requires);
        Assert.Equal(new[] { "own:p1" }, Data.Perks[1].Ranks[0].Requires);
        Assert.Null(Data.Perks[2].Ranks[0].Requires);
    }

    [Fact]
    public void Generic_data_uses_the_owned_perk_checker_so_p2_needs_p1()
    {
        var requirement = Data.Perks[1].Ranks[0].Requires![0];

        Assert.IsType<OwnedPerkRequirementChecker>(Data.PerkRequirements);
        Assert.False(Data.PerkRequirements.IsMet(requirement, new Dictionary<string, int>()));
        Assert.True(Data.PerkRequirements.IsMet(requirement, new Dictionary<string, int> { ["p1"] = 1 }));
    }

    [Fact]
    public void Generic_data_derived_stats_compute_exact_values()
    {
        Assert.Equal(new[] { "d1", "d2" }, Data.DerivedStats.Select(d => d.Id));
        Assert.Equal(new[] { "Total of 1 and 2", "Double of 3" }, Data.DerivedStats.Select(d => d.Label));
        Assert.All(Data.DerivedStats, d => Assert.True(d.HigherIsBetter));

        var values = new Dictionary<string, double> { ["s1"] = 3, ["s2"] = 4.5, ["s3"] = 4 };

        Assert.Equal(7.5, Data.DerivedStats[0].Formula(values));
        Assert.Equal(8.0, Data.DerivedStats[1].Formula(values));
    }
}
