using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PerkChartEdgeTests
{
    static readonly string[] Names = { "Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck" };

    static PerkChartEntry Attr(int column, int value = 5) =>
        new($"a{column}", Names[column], PerkChartKind.Attribute, column, value, 10,
            Enumerable.Range(1, 10).Select(i => new PerkRankInfo($"{Names[column]} {i}")).ToArray(), true);

    static PerkChartEntry Perk(string id, string name, int? column, int current, int max, bool available, params UnmetRequirement[] unmet) =>
        new(id, name, PerkChartKind.Perk, column, current, max,
            Enumerable.Range(1, max).Select(i => new PerkRankInfo($"{name} rank {i}", 0, null, unmet)).ToArray(), available);

    static PerkChartSnapshot Snap(int points, params PerkChartEntry[] perks) =>
        new(1, 4, points, new[] { 5, 5, 5, 5, 5, 5, 5 }, Enumerable.Range(0, 7).Select(c => Attr(c)).Concat(perks).ToList());

    static (PerkChartController Chart, List<string> Sounds, List<int> Changes) Make(PerkChartMode mode, PerkChartSnapshot snapshot)
    {
        var chart = new PerkChartController(mode);
        var sounds = new List<string>();
        var changes = new List<int>();
        chart.SoundRequested += sounds.Add;
        chart.Changed += () => changes.Add(changes.Count);
        chart.SetSnapshot(snapshot);
        sounds.Clear();
        changes.Clear();
        return (chart, sounds, changes);
    }

    [Fact]
    public void A_controller_without_a_snapshot_reports_neutral_defaults()
    {
        var chart = new PerkChartController();

        Assert.Equal(-1, chart.Revision);
        Assert.Equal(0, chart.Level);
        Assert.Equal(0, chart.PerkPoints);
        Assert.Equal("Level 0", chart.HeaderLine);
        Assert.Equal("Perk points: 0", chart.PointsLine);
        Assert.Equal(PerkChartController.NoPerksMessage, chart.EmptyMessage);
        Assert.Empty(chart.Columns);
        Assert.Null(chart.SelectedId);
        Assert.Equal(string.Empty, chart.DetailName);
        Assert.Equal(string.Empty, chart.DetailDescription);
        Assert.Equal(string.Empty, chart.ConfirmPrompt);
        Assert.Null(chart.ViewingLine);
        Assert.Empty(chart.RequirementLines);
        Assert.Empty(chart.Pips);
        Assert.False(chart.CanChoose);
        Assert.Equal(1, chart.ViewedRank);
        Assert.Equal(new[] { "Close", "Previous rank", "Next rank", "Show only available" }, chart.Hints.Select(h => h.Label));
        Assert.Null(new PerkChartController(PerkChartMode.Browse).PointsLine);
    }

    [Fact]
    public void Changing_the_mode_raises_changed_once_and_browse_drops_a_pending_confirmation()
    {
        var (chart, sounds, changes) = Make(PerkChartMode.Spend, Snap(1, Perk("iron", "Iron Fist", 0, 0, 1, true)));
        chart.Select("iron");
        chart.Accept();
        sounds.Clear();
        changes.Clear();

        chart.SetMode(PerkChartMode.Spend);
        Assert.Empty(changes);
        Assert.True(chart.IsConfirming);

        chart.SetMode(PerkChartMode.Browse);
        Assert.Single(changes);
        Assert.False(chart.IsConfirming);
        Assert.Empty(sounds);
        Assert.Equal(PerkChartMode.Browse, chart.Mode);
    }

    [Fact]
    public void While_confirming_the_hints_are_confirm_and_cancel_and_every_other_input_is_swallowed()
    {
        var (chart, sounds, _) = Make(PerkChartMode.Spend, Snap(1, Perk("iron", "Iron Fist", 0, 0, 3, true)));
        chart.Select("iron");
        chart.Accept();
        sounds.Clear();

        Assert.Equal(new[] { (MenuInputAction.Accept, "Confirm"), (MenuInputAction.Back, "Cancel") }, chart.Hints.Select(h => (h.Action, h.Label)));
        Assert.True(chart.Handle(MenuInputAction.Down));
        Assert.True(chart.Handle(MenuInputAction.Favourite));
        Assert.True(chart.Handle(MenuInputAction.TabNext));
        chart.ToggleAvailableOnly();
        chart.ViewRank(1);
        Assert.False(chart.Select("a0"));
        Assert.Equal("iron", chart.SelectedId);
        Assert.False(chart.AvailableOnly);
        Assert.Equal(1, chart.ViewedRank);
        Assert.Empty(sounds);

        Assert.True(chart.Handle(MenuInputAction.Back));
        Assert.False(chart.IsConfirming);
        Assert.Equal(new[] { "cancel" }, sounds);
    }

    [Fact]
    public void The_choose_hint_needs_spend_mode_and_a_rank_left_and_the_filter_hint_flips_its_label()
    {
        var snapshot = Snap(2, Perk("done", "Done Perk", 0, 1, 1, true));
        var (spend, _, _) = Make(PerkChartMode.Spend, snapshot);

        Assert.Equal(new[] { "Choose", "Close", "Previous rank", "Next rank", "Show only available" }, spend.Hints.Select(h => h.Label));
        spend.Select("done");
        Assert.Equal(new[] { "Close", "Previous rank", "Next rank", "Show only available" }, spend.Hints.Select(h => h.Label));
        spend.Handle(MenuInputAction.Compare);
        Assert.Equal("Show all", spend.Hints.Last().Label);
        Assert.Equal(MenuInputAction.Compare, spend.Hints.Last().Action);

        var (browse, _, _) = Make(PerkChartMode.Browse, snapshot);
        Assert.DoesNotContain(browse.Hints, h => h.Action == MenuInputAction.Accept);
    }

    [Theory]
    [InlineData(MenuInputAction.Search)]
    [InlineData(MenuInputAction.Sort)]
    [InlineData(MenuInputAction.PageUp)]
    [InlineData(MenuInputAction.PageDown)]
    public void Actions_the_chart_does_not_use_are_not_handled_and_change_nothing(MenuInputAction action)
    {
        var (chart, sounds, changes) = Make(PerkChartMode.Spend, Snap(1, Perk("iron", "Iron Fist", 0, 0, 1, true)));

        Assert.False(chart.Handle(action));
        Assert.Empty(sounds);
        Assert.Empty(changes);
        Assert.Equal("a0", chart.SelectedId);
    }

    [Fact]
    public void A_shown_error_lasts_two_seconds_restarts_on_a_new_one_and_ignores_non_positive_ticks()
    {
        var (chart, _, changes) = Make(PerkChartMode.Browse, Snap(0));

        chart.ShowError("Boom");
        Assert.Equal("Boom", chart.ErrorLine);
        chart.Tick(0);
        chart.Tick(-5);
        chart.Tick(double.NaN);
        Assert.Equal("Boom", chart.ErrorLine);
        chart.Tick(1.5);
        chart.ShowError("Again");
        chart.Tick(1.5);
        Assert.Equal("Again", chart.ErrorLine);
        Assert.Equal(2, changes.Count);
        chart.Tick(0.6);
        Assert.Equal(string.Empty, chart.ErrorLine);
        Assert.Equal(3, changes.Count);
    }

    [Fact]
    public void An_other_kind_requirement_is_described_by_its_name_alone()
    {
        var (chart, _, _) = Make(PerkChartMode.Spend, Snap(1,
            Perk("vault", "Vault Perk", 0, 0, 1, false, new UnmetRequirement(UnmetKind.Other, 0, 0, "Vault key"))));
        chart.Select("vault");

        Assert.Equal(new[] { "Requires Vault key" }, chart.RequirementLines);
    }

    [Fact]
    public void Calls_that_do_not_apply_to_the_current_state_are_silent_no_ops()
    {
        var (chart, sounds, changes) = Make(PerkChartMode.Spend, Snap(1, Perk("iron", "Iron Fist", 0, 0, 2, true)));
        var choices = 0;
        chart.ChoiceRequested += (_, _) => choices++;
        chart.Select("iron");
        sounds.Clear();
        changes.Clear();

        chart.Confirm();
        chart.CancelConfirm();
        chart.Dismiss();
        chart.Unlock();
        Assert.True(chart.Select("iron"));
        Assert.False(chart.Select("missing"));
        Assert.Equal(0, choices);
        Assert.Empty(sounds);
        Assert.Empty(changes);
    }

    [Fact]
    public void Viewing_a_rank_clamps_to_the_first_and_last_rank_and_only_a_real_change_makes_a_sound()
    {
        var (chart, sounds, _) = Make(PerkChartMode.Spend, Snap(1, Perk("iron", "Iron Fist", 0, 0, 3, true)));
        chart.Select("iron");
        sounds.Clear();

        chart.ViewRank(-1);
        Assert.Equal(1, chart.ViewedRank);
        Assert.Empty(sounds);
        chart.ViewRank(9);
        Assert.Equal(3, chart.ViewedRank);
        chart.ViewRank(1);
        Assert.Equal(3, chart.ViewedRank);
        Assert.Equal(new[] { "rank.view" }, sounds);
        chart.ViewRank(-9);
        Assert.Equal(1, chart.ViewedRank);
        Assert.Equal(new[] { "rank.view", "rank.view" }, sounds);
    }

    [Fact]
    public void Columns_without_an_attribute_head_get_a_numbered_title_and_stray_columns_land_in_other()
    {
        var entries = new List<PerkChartEntry>
        {
            Attr(0),
            Perk("p2", "Second", 2, 0, 1, true),
            Perk("zed", "Zed", null, 0, 1, true),
            Perk("neg", "alpha", -1, 0, 1, true),
            Perk("far", "Beta", 9, 0, 1, true),
        };
        var chart = new PerkChartController();
        chart.SetSnapshot(new PerkChartSnapshot(1, 1, 0, new[] { 4, 6 }, entries));

        Assert.Equal(new[] { 0, 2, 7 }, chart.Columns.Select(c => c.Index));
        Assert.Equal(new[] { "Strength", "Column 3", "Other" }, chart.Columns.Select(c => c.Title));
        Assert.Equal(new int?[] { 4, null, null }, chart.Columns.Select(c => c.Value));
        Assert.Equal(new[] { "neg", "far", "zed" }, chart.Columns[2].Rows.Select(e => e.Id));
    }
}
