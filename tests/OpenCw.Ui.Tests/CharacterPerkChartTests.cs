using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterPerkChartTests
{
    static readonly string[] Names = { "Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck" };

    sealed class Rig
    {
        public PerkChartController Chart = null!;
        public List<(string Id, int Rank)> Choices = new();
        public List<string> Sounds = new();
        public List<string> Rejected = new();
        public int Closes;
    }

    static PerkChartEntry Attr(int column, int value = 5, bool available = true) =>
        new($"a{column}", Names[column], PerkChartKind.Attribute, column, value, 10,
            Enumerable.Range(1, 10).Select(i => new PerkRankInfo($"{Names[column]} {i}")).ToArray(), available);

    static PerkChartEntry Perk(string id, string name, int? column, int current, int max, bool available, int attribute = 0, int level = 0,
                               params UnmetRequirement[] unmet) =>
        new(id, name, PerkChartKind.Perk, column, current, max,
            Enumerable.Range(1, max).Select(i => new PerkRankInfo($"{name} rank {i}", level, column is { } c ? (c, attribute) : null, unmet)).ToArray(),
            available);

    static List<PerkChartEntry> Entries(bool withOther = true)
    {
        var list = Enumerable.Range(0, 7).Select(c => Attr(c)).ToList();
        list.Add(Perk("big", "Big League", 0, 1, 2, true, 5, 5));
        list.Add(Perk("zeta", "Zeta", 0, 2, 2, false, 2, 3));
        list.Add(Perk("iron", "Iron Fist", 0, 0, 3, true, 2, 1));
        list.Add(Perk("alpha", "alpha", 0, 0, 1, false, 2, 3));
        list.Add(Perk("locked", "Locked Perk", 1, 0, 1, false, 4, 14,
            new UnmetRequirement(UnmetKind.Level, 14, 11, ""),
            new UnmetRequirement(UnmetKind.Attribute, 6, 4, "Strength"),
            new UnmetRequirement(UnmetKind.Perk, 2, 0, "Locksmith")));
        if (withOther) list.Add(Perk("mod", "Mod Perk", null, 0, 1, true));
        return list;
    }

    static PerkChartSnapshot Snap(int revision, int points, IReadOnlyList<PerkChartEntry> entries) =>
        new(revision, 7, points, new[] { 5, 5, 5, 5, 5, 5, 5 }, entries);

    static Rig Make(PerkChartMode mode = PerkChartMode.Spend, int points = 2, int revision = 1, bool withOther = true)
    {
        var rig = new Rig { Chart = new PerkChartController(mode) };
        rig.Chart.ChoiceRequested += (id, rank) => rig.Choices.Add((id, rank));
        rig.Chart.SoundRequested += rig.Sounds.Add;
        rig.Chart.EntryRejected += rig.Rejected.Add;
        rig.Chart.CloseRequested += () => rig.Closes++;
        rig.Chart.SetSnapshot(Snap(revision, points, Entries(withOther)));
        return rig;
    }

    [Fact]
    public void An_empty_model_shows_the_message_selects_nothing_and_still_closes()
    {
        var rig = new Rig { Chart = new PerkChartController() };
        rig.Chart.CloseRequested += () => rig.Closes++;
        rig.Chart.SetSnapshot(Snap(1, 3, new List<PerkChartEntry>()));

        Assert.Equal("No perks are available yet.", rig.Chart.EmptyMessage);
        Assert.Null(rig.Chart.Selected);
        Assert.False(rig.Chart.CanChoose);
        rig.Chart.Accept();
        Assert.False(rig.Chart.IsConfirming);
        Assert.True(rig.Chart.Handle(MenuInputAction.Back));
        Assert.Equal(1, rig.Closes);
    }

    [Fact]
    public void Columns_follow_the_seven_attributes_sort_perks_by_requirement_and_add_other_only_when_needed()
    {
        var rig = Make();
        var columns = rig.Chart.Columns;

        Assert.Equal(8, columns.Count);
        Assert.Equal(new[] { "Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck", "Other" }, columns.Select(c => c.Title));
        Assert.Equal(5, columns[0].Value);
        Assert.Null(columns[7].Value);
        Assert.Equal(new[] { "a0", "iron", "alpha", "zeta", "big" }, columns[0].Rows.Select(e => e.Id));
        Assert.Equal(new[] { "a1", "locked" }, columns[1].Rows.Select(e => e.Id));
        Assert.Equal("a0", rig.Chart.SelectedId);

        Assert.Equal(7, Make(withOther: false).Chart.Columns.Count);
    }

    [Fact]
    public void Badge_words_and_rank_text_match_the_entry_state()
    {
        var entries = Entries();

        Assert.Equal("OWNED", PerkChartController.Badge(entries.First(e => e.Id == "zeta")));
        Assert.Equal("IN PROGRESS", PerkChartController.Badge(entries.First(e => e.Id == "big")));
        Assert.Equal("AVAILABLE", PerkChartController.Badge(entries.First(e => e.Id == "iron")));
        Assert.Equal("LOCKED", PerkChartController.Badge(entries.First(e => e.Id == "locked")));
        Assert.Equal("1 / 2", PerkChartController.RankText(entries.First(e => e.Id == "big")));
        Assert.Null(PerkChartController.RankText(entries.First(e => e.Id == "locked")));
    }

    [Fact]
    public void The_viewed_rank_defaults_to_the_next_rank_and_viewing_never_changes_the_selection_or_model()
    {
        var rig = Make();
        rig.Chart.Select("iron");

        Assert.Equal(1, rig.Chart.ViewedRank);
        Assert.Null(rig.Chart.ViewingLine);
        Assert.Equal("Iron Fist rank 1", rig.Chart.DetailDescription);
        Assert.Equal("IRON FIST", rig.Chart.DetailName);

        rig.Chart.ViewRank(1);
        Assert.Equal(2, rig.Chart.ViewedRank);
        Assert.Equal("Viewing rank 2 of 3 (not your next rank)", rig.Chart.ViewingLine);
        rig.Chart.ViewRank(1);
        rig.Chart.ViewRank(1);
        Assert.Equal(3, rig.Chart.ViewedRank);
        rig.Chart.ViewRank(-5);
        rig.Chart.ViewRank(-1);
        Assert.Equal(1, rig.Chart.ViewedRank);
        Assert.Equal("iron", rig.Chart.SelectedId);
        Assert.Equal(3, rig.Sounds.Count(s => s == "rank.view"));

        rig.Chart.Select("big");
        Assert.Equal(2, rig.Chart.ViewedRank);
        rig.Chart.Select("zeta");
        Assert.Equal(2, rig.Chart.ViewedRank);
        Assert.Equal("Viewing rank 2 of 2 (already taken)", rig.Chart.ViewingLine);
        Assert.Empty(rig.Chart.RequirementLines);
        rig.Chart.Handle(MenuInputAction.TabPrev);
        Assert.Equal(1, rig.Chart.ViewedRank);
        Assert.Equal("Viewing rank 1 of 2 (already taken)", rig.Chart.ViewingLine);
    }

    [Fact]
    public void Pips_show_owned_next_and_empty_ranks_and_hide_the_outline_in_browse_mode_or_without_points()
    {
        var spend = Make();
        spend.Chart.Select("big");
        Assert.Equal(new[] { PipState.Filled, PipState.Outlined }, spend.Chart.Pips);
        spend.Chart.Select("iron");
        Assert.Equal(new[] { PipState.Outlined, PipState.Empty, PipState.Empty }, spend.Chart.Pips);
        spend.Chart.Select("locked");
        Assert.Empty(spend.Chart.Pips);

        var browse = Make(PerkChartMode.Browse);
        browse.Chart.Select("big");
        Assert.Equal(new[] { PipState.Filled, PipState.Empty }, browse.Chart.Pips);

        var broke = Make(points: 0);
        broke.Chart.Select("big");
        Assert.Equal(new[] { PipState.Filled, PipState.Empty }, broke.Chart.Pips);
    }

    [Fact]
    public void Requirement_lines_list_each_unmet_condition_for_the_viewed_rank()
    {
        var rig = Make();
        rig.Chart.Select("locked");

        Assert.Equal(new[]
        {
            "Requires level 14 (you are 11)",
            "Requires Strength 6 (you have 4)",
            "Requires Locksmith rank 2",
        }, rig.Chart.RequirementLines);
    }

    [Fact]
    public void Navigation_clamps_at_the_edges_never_wraps_and_keeps_the_row_across_columns()
    {
        var rig = Make();
        var selections = new List<string>();
        rig.Chart.SelectionChanged += selections.Add;

        rig.Chart.Handle(MenuInputAction.Up);
        rig.Chart.Handle(MenuInputAction.Left);
        Assert.Equal("a0", rig.Chart.SelectedId);
        Assert.Empty(selections);

        for (var i = 0; i < 10; i++) rig.Chart.Handle(MenuInputAction.Down);
        Assert.Equal("big", rig.Chart.SelectedId);

        rig.Chart.Handle(MenuInputAction.Right);
        Assert.Equal("locked", rig.Chart.SelectedId);
        rig.Chart.Handle(MenuInputAction.Right);
        Assert.Equal("a2", rig.Chart.SelectedId);
        for (var i = 0; i < 8; i++) rig.Chart.Handle(MenuInputAction.Right);
        Assert.Equal("mod", rig.Chart.SelectedId);
        Assert.Contains("focus.move", rig.Sounds);
    }

    [Fact]
    public void Accept_in_spend_mode_follows_the_state_rules_and_the_error_line_expires_after_two_seconds()
    {
        var rig = Make();

        rig.Chart.Select("locked");
        rig.Chart.Accept();
        Assert.Equal("Requirements not met", rig.Chart.ErrorLine);
        Assert.Contains("cancel", rig.Sounds);
        rig.Chart.Tick(1.9);
        Assert.Equal("Requirements not met", rig.Chart.ErrorLine);
        rig.Chart.Tick(0.2);
        Assert.Equal(string.Empty, rig.Chart.ErrorLine);

        rig.Chart.Select("zeta");
        rig.Chart.Accept();
        Assert.Equal(string.Empty, rig.Chart.ErrorLine);
        Assert.False(rig.Chart.IsConfirming);

        rig.Chart.Select("iron");
        rig.Chart.ViewRank(1);
        rig.Chart.Accept();
        Assert.False(rig.Chart.IsConfirming);
        Assert.False(rig.Chart.CanChoose);

        rig.Chart.SetSnapshot(Snap(2, 0, Entries()));
        rig.Sounds.Clear();
        rig.Chart.Select("iron");
        rig.Chart.Accept();
        Assert.Equal("No perk points to spend", rig.Chart.ErrorLine);
        Assert.DoesNotContain("cancel", rig.Sounds);
        Assert.False(rig.Chart.IsConfirming);
    }

    [Fact]
    public void Confirming_emits_one_choice_ignores_further_accepts_until_a_newer_model_and_cancel_leaves_it()
    {
        var rig = Make();
        rig.Chart.Select("iron");

        rig.Chart.Accept();
        Assert.True(rig.Chart.IsConfirming);
        Assert.Equal(new[] { "Take Iron Fist rank 1? Points after: 1" }, rig.Chart.RequirementLines);
        rig.Chart.Handle(MenuInputAction.Right);
        Assert.Equal("iron", rig.Chart.SelectedId);
        rig.Chart.Handle(MenuInputAction.Back);
        Assert.False(rig.Chart.IsConfirming);
        Assert.Equal(0, rig.Closes);
        Assert.Empty(rig.Choices);

        rig.Chart.Accept();
        rig.Chart.Handle(MenuInputAction.Accept);
        Assert.Equal(new[] { ("iron", 1) }, rig.Choices);
        Assert.Contains("perk.first", rig.Sounds);
        Assert.True(rig.Chart.IsAwaitingModel);

        rig.Chart.Accept();
        rig.Chart.Accept();
        Assert.Single(rig.Choices);

        rig.Chart.SetSnapshot(Snap(1, 1, Entries()));
        Assert.True(rig.Chart.IsAwaitingModel);
        rig.Chart.SetSnapshot(Snap(2, 1, Entries()));
        Assert.False(rig.Chart.IsAwaitingModel);
        rig.Chart.Accept();
        Assert.True(rig.Chart.IsConfirming);
    }

    [Fact]
    public void A_later_rank_requests_perk_rank_and_an_unanswered_choice_times_out_after_three_seconds()
    {
        var rig = Make();
        rig.Chart.Select("big");
        rig.Chart.Accept();
        rig.Chart.Confirm();

        Assert.Equal(new[] { ("big", 2) }, rig.Choices);
        Assert.Contains("perk.rank", rig.Sounds);
        rig.Chart.Tick(2.9);
        Assert.True(rig.Chart.IsAwaitingModel);
        rig.Chart.Tick(0.2);
        Assert.False(rig.Chart.IsAwaitingModel);
        Assert.Equal("Could not apply that choice", rig.Chart.ErrorLine);
    }

    [Fact]
    public void Browse_mode_never_asks_for_a_choice_and_shows_points_only_when_there_are_some()
    {
        var rig = Make(PerkChartMode.Browse, points: 0);
        rig.Chart.Select("iron");

        rig.Chart.Accept();
        rig.Chart.Handle(MenuInputAction.Accept);
        Assert.False(rig.Chart.IsConfirming);
        Assert.Empty(rig.Choices);
        Assert.Null(rig.Chart.PointsLine);
        Assert.DoesNotContain(rig.Chart.Hints, h => h.Action == MenuInputAction.Accept);

        rig.Chart.SetSnapshot(Snap(2, 2, Entries()));
        Assert.Equal("Perk points: 2", rig.Chart.PointsLine);
        Assert.Equal("Level 7", rig.Chart.HeaderLine);
    }

    [Fact]
    public void An_older_revision_is_ignored_and_selection_is_kept_by_id_or_the_same_column()
    {
        var rig = Make(revision: 5);
        rig.Chart.Select("big");

        Assert.False(rig.Chart.SetSnapshot(Snap(4, 0, new List<PerkChartEntry>())));
        Assert.Equal("big", rig.Chart.SelectedId);
        Assert.Equal(5, rig.Chart.Revision);

        Assert.True(rig.Chart.SetSnapshot(Snap(6, 2, Entries().Where(e => e.Id != "iron").ToList())));
        Assert.Equal("big", rig.Chart.SelectedId);

        Assert.True(rig.Chart.SetSnapshot(Snap(7, 2, Entries().Where(e => e.Id != "iron" && e.Id != "big").ToList())));
        Assert.Contains(rig.Chart.Columns[0].Rows, e => e.Id == rig.Chart.SelectedId);
    }

    [Fact]
    public void Invalid_entries_are_dropped_and_reported_once_while_the_chart_still_opens()
    {
        var bad = new List<PerkChartEntry>(Entries())
        {
            Perk("over", "Over", 2, 3, 2, false),
            Perk("blank", " ", 2, 0, 1, false),
            Perk("zero", "Zero", 2, 0, 0, false),
            Perk("short", "Short", 2, 0, 2, false) with { Ranks = new[] { new PerkRankInfo("only") } },
        };
        var rig = Make();
        rig.Chart.SetSnapshot(Snap(2, 2, bad));

        Assert.Equal(new[] { "over", "blank", "zero", "short" }, rig.Rejected);
        Assert.DoesNotContain(rig.Chart.Columns.SelectMany(c => c.Rows), e => e.Id is "over" or "blank" or "zero" or "short");
        rig.Chart.SetSnapshot(Snap(3, 2, bad));
        Assert.Equal(4, rig.Rejected.Count);
        Assert.Equal("a0", rig.Chart.SelectedId);
    }

    [Fact]
    public void The_available_filter_keeps_attributes_moves_selection_and_reports_when_nothing_is_available()
    {
        var rig = Make();
        rig.Chart.Select("locked");

        rig.Chart.Handle(MenuInputAction.Compare);
        Assert.True(rig.Chart.AvailableOnly);
        Assert.Equal("a1", rig.Chart.SelectedId);
        Assert.Equal(new[] { "a0", "iron", "big" }, rig.Chart.Columns[0].Rows.Select(e => e.Id));
        Assert.Null(rig.Chart.EmptyMessage);

        rig.Chart.Handle(MenuInputAction.Favourite);
        Assert.False(rig.Chart.AvailableOnly);

        var none = Make();
        none.Chart.SetSnapshot(Snap(2, 2, Entries().Where(e => e.Kind == PerkChartKind.Attribute || e.Id == "locked").ToList()));
        none.Chart.ToggleAvailableOnly();
        Assert.Equal("Nothing is available to take", none.Chart.EmptyMessage);
        Assert.Equal(7, none.Chart.Columns.Count);
    }

    [Fact]
    public void A_confirmation_is_cancelled_when_a_new_model_makes_the_choice_unavailable_and_kept_otherwise()
    {
        var rig = Make();
        rig.Chart.Select("iron");
        rig.Chart.Accept();

        rig.Chart.SetSnapshot(Snap(2, 2, Entries()));
        Assert.True(rig.Chart.IsConfirming);
        Assert.Equal("iron", rig.Chart.SelectedId);

        rig.Chart.SetSnapshot(Snap(3, 2, Entries().Select(e => e.Id == "iron" ? e with { Available = false } : e).ToList()));
        Assert.False(rig.Chart.IsConfirming);
        Assert.Equal("That choice is no longer available", rig.Chart.ErrorLine);
    }

    [Fact]
    public void Closing_while_confirming_dismisses_silently_and_new_perk_marks_are_remembered()
    {
        var rig = Make();
        rig.Chart.Select("iron");
        rig.Chart.Accept();
        rig.Sounds.Clear();

        rig.Chart.Dismiss();
        Assert.False(rig.Chart.IsConfirming);
        Assert.Empty(rig.Sounds);

        rig.Chart.MarkNew(new[] { "big" });
        Assert.True(rig.Chart.IsNew("big"));
        Assert.False(rig.Chart.IsNew("iron"));
    }

    static List<PerkChartEntry> WithPriced(int cost)
    {
        var list = Enumerable.Range(0, 7).Select(c => Attr(c)).ToList();
        list.Add(Priced(cost));
        return list;
    }

    static PerkChartEntry Priced(int cost) =>
        new("pricey", "Pricey", PerkChartKind.Perk, 0, 0, 1, new[] { new PerkRankInfo("Costs more", Cost: cost) }, true);

    [Fact]
    public void A_rank_that_costs_more_than_the_points_in_hand_cannot_be_chosen_and_the_prompt_uses_its_cost()
    {
        var rig = new Rig { Chart = new PerkChartController() };
        rig.Chart.SetSnapshot(Snap(1, 2, WithPriced(3)));
        rig.Chart.Select("pricey");

        Assert.False(rig.Chart.CanChoose);
        rig.Chart.Accept();
        Assert.False(rig.Chart.IsConfirming);
        Assert.Equal("No perk points to spend", rig.Chart.ErrorLine);

        rig.Chart.SetSnapshot(Snap(2, 5, WithPriced(3)));
        Assert.True(rig.Chart.CanChoose);
        rig.Chart.Accept();
        Assert.Equal("Take Pricey rank 1? Points after: 2", rig.Chart.ConfirmPrompt);
    }

    [Fact]
    public void A_snapshot_that_shows_the_chosen_rank_unlocks_even_without_a_newer_revision()
    {
        var rig = Make();
        rig.Chart.Select("iron");
        rig.Chart.Accept();
        rig.Chart.Confirm();
        Assert.True(rig.Chart.IsAwaitingModel);

        var taken = Entries().Select(e => e.Id == "iron" ? e with { CurrentRank = 1 } : e).ToList();
        rig.Chart.SetSnapshot(Snap(1, 1, taken));

        Assert.False(rig.Chart.IsAwaitingModel);
    }
}
