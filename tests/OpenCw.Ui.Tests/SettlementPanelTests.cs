using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettlementPanelTests
{
    static (SettlementPanelController Panel, FakeSettlements Provider) Make(bool embedded = false, string? start = null)
    {
        var provider = new FakeSettlements();
        return (new SettlementPanelController(provider, embedded, start), provider);
    }

    static void Press(SettlementPanelController panel, params MenuInputAction[] actions)
    {
        foreach (var action in actions) panel.Handle(action);
    }

    static string[] Rows(SettlementPanelController panel) => WorkshopTestHelpers.RowTexts(panel.List);

    static (SettlementPanelController Panel, FakeSettlements Provider) AtActions(string settler = "a")
    {
        var (panel, provider) = Make();
        Press(panel, MenuInputAction.Accept, MenuInputAction.TabNext);
        if (settler == "b") panel.Handle(MenuInputAction.Down);
        panel.Handle(MenuInputAction.Accept);
        Assert.Equal(PanelLevel.Actions, panel.Level);
        return (panel, provider);
    }

    [Fact]
    public void The_first_level_lists_settlements_and_marks_one_under_attack()
    {
        var (panel, _) = Make();

        Assert.Equal(new[] { "Sanctuary", "Hill - Under attack" }, Rows(panel));
        Assert.Equal(PanelLevel.Settlements, panel.Level);
    }

    [Fact]
    public void Opening_a_settlement_shows_its_overview_metrics_and_a_missing_value_reads_as_dashes()
    {
        var (panel, _) = Make();

        panel.Handle(MenuInputAction.Accept);

        Assert.Equal(PanelLevel.Pane, panel.Level);
        Assert.Equal(new[] { "Food: 12.5", "Water: --" }, Rows(panel));
        Assert.Equal("Sanctuary - Overview", panel.Header);
    }

    [Fact]
    public void Tabs_cycle_through_overview_settlers_and_routes_and_wrap()
    {
        var (panel, _) = Make();
        panel.Handle(MenuInputAction.Accept);

        panel.Handle(MenuInputAction.TabNext);
        Assert.Equal(SettlementTab.Settlers, panel.Tab);
        Assert.Equal(new[] { "Ann - Farming", "Bob - Unassigned" }, Rows(panel));

        panel.Handle(MenuInputAction.Right);
        Assert.Equal(new[] { "Sanctuary to Hill" }, Rows(panel));

        panel.Handle(MenuInputAction.TabNext);
        Assert.Equal(SettlementTab.Overview, panel.Tab);

        panel.Handle(MenuInputAction.Left);
        Assert.Equal(SettlementTab.Routes, panel.Tab);
    }

    [Fact]
    public void Empty_lists_say_so()
    {
        var (panel, provider) = Make();
        provider.SettlerList.Clear();
        Press(panel, MenuInputAction.Accept, MenuInputAction.TabNext);

        Assert.Equal(SettlementPanelController.NoSettlersText, panel.DetailLines.Single().Text);

        var empty = new FakeSettlements();
        empty.SettlementList.Clear();
        var none = new SettlementPanelController(empty);

        Assert.Equal(SettlementPanelController.NoSettlementsText, none.DetailLines.Single().Text);
    }

    [Fact]
    public void A_settler_opens_an_action_list_and_unassign_is_disabled_when_there_is_nothing_to_unassign()
    {
        var (panel, _) = AtActions("b");

        Assert.Equal(new[] { "Move to another settlement", "Assign caravan route", "Assign job", "Unassign" }, Rows(panel));

        Press(panel, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Accept);

        Assert.Equal("Nothing to unassign.", panel.Message);
        Assert.False(panel.IsConfirming);
    }

    [Fact]
    public void A_disabled_action_says_the_providers_reason()
    {
        var (panel, provider) = Make();
        provider.SettlerList[0] = provider.SettlerList[0] with { CanMove = false, MoveReason = "Ann is in combat." };
        Press(panel, MenuInputAction.Accept, MenuInputAction.TabNext, MenuInputAction.Accept);

        panel.Handle(MenuInputAction.Accept);

        Assert.Equal("Ann is in combat.", panel.Message);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public void Moving_a_settler_goes_through_a_picker_that_excludes_the_current_home_and_reports_the_change()
    {
        var (panel, provider) = AtActions();
        DestinationPickerController? picker = null;
        var changes = new List<(string, SettlerChange)>();
        panel.PickerRequested += p => picker = p;
        panel.SettlerChanged += (id, change) => changes.Add((id, change));

        panel.Handle(MenuInputAction.Accept);

        Assert.NotNull(picker);
        Assert.Equal(new[] { "Hill" }, WorkshopTestHelpers.RowTexts(picker!.List));
        picker.Handle(MenuInputAction.Accept);
        picker.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "home:a>s2" }, provider.Calls);
        Assert.Equal(new[] { ("a", SettlerChange.Home) }, changes);
        Assert.Equal(SettlementPanelController.DoneText, panel.Message);
        Assert.True(picker.IsConfirmed);
    }

    [Theory]
    [InlineData("Tower", true)]
    [InlineData("Bed", false)]
    public void An_occupied_target_names_its_owner_before_confirming_a_transfer(string name, bool needsWorker)
    {
        var (panel, provider) = AtActions();
        provider.JobTargets[1] = new JobTarget("t2", name, needsWorker, "Cy");
        provider.Next = new(true, "Cy was unassigned.");
        DestinationPickerController? picker = null;
        panel.PickerRequested += p => picker = p;

        Press(panel, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Accept);
        Assert.NotNull(picker);
        picker!.Handle(MenuInputAction.Down);
        picker.Handle(MenuInputAction.Accept);

        Assert.Contains(name + " (in use by Cy)", picker.Header);
        Assert.Empty(provider.Calls);
        picker.Handle(MenuInputAction.Accept);
        Assert.Equal(new[] { "job:a>t2" }, provider.Calls);
        Assert.Contains("Cy was unassigned.", panel.Message);
    }

    [Fact]
    public void Unassign_asks_inline_cancel_does_nothing_and_confirm_calls_the_provider_once()
    {
        var (panel, provider) = AtActions();
        var changes = new List<SettlerChange>();
        panel.SettlerChanged += (_, change) => changes.Add(change);
        Press(panel, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Accept);

        Assert.True(panel.IsConfirming);
        panel.Handle(MenuInputAction.Back);
        Assert.False(panel.IsConfirming);
        Assert.Empty(provider.Calls);

        Press(panel, MenuInputAction.Accept, MenuInputAction.Accept);

        Assert.Equal(new[] { "unassign:a" }, provider.Calls);
        Assert.Equal(new[] { SettlerChange.Unassigned }, changes);
        Assert.False(panel.IsConfirming);
        Assert.Equal(PanelLevel.Actions, panel.Level);
    }

    [Fact]
    public void A_failed_command_shows_the_reason_and_raises_no_change()
    {
        var (panel, provider) = AtActions();
        var changes = 0;
        panel.SettlerChanged += (_, _) => changes++;
        provider.Next = new ActionResult(false, "Not now.");
        Press(panel, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Accept, MenuInputAction.Accept);

        Assert.Equal("Not now.", panel.Message);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Removing_a_route_confirms_calls_the_provider_and_returns_to_the_pane_when_the_route_is_gone()
    {
        var (panel, provider) = Make();
        Press(panel, MenuInputAction.Accept, MenuInputAction.TabNext, MenuInputAction.TabNext, MenuInputAction.Accept);
        Assert.Equal(new[] { "Remove route" }, Rows(panel));

        Press(panel, MenuInputAction.Accept, MenuInputAction.Accept);

        Assert.Equal(new[] { "remove:r1" }, provider.Calls);
        Assert.Equal(PanelLevel.Pane, panel.Level);
        Assert.Equal(SettlementPanelController.NoRoutesText, panel.DetailLines.Single().Text);
    }

    [Fact]
    public void A_provider_change_that_removes_the_settler_returns_the_panel_to_the_pane()
    {
        var (panel, provider) = AtActions();

        provider.SettlerList.RemoveAt(0);
        provider.Raise();

        Assert.Equal(PanelLevel.Pane, panel.Level);
        Assert.Equal(new[] { "Bob - Unassigned" }, Rows(panel));
    }

    [Fact]
    public void A_provider_change_keeps_the_selection_by_key()
    {
        var (panel, provider) = Make();
        Press(panel, MenuInputAction.Accept, MenuInputAction.TabNext, MenuInputAction.Down);
        Assert.Equal("b", panel.Rows.Selected!.Key);

        provider.SettlerList.Insert(0, new SettlerInfo("z", "Zed", "s1"));
        provider.Raise();

        Assert.Equal("b", panel.Rows.Selected!.Key);
    }

    [Fact]
    public void Back_steps_up_one_level_at_a_time_and_closes_from_the_top()
    {
        var (panel, _) = AtActions();

        panel.Handle(MenuInputAction.Back);
        Assert.Equal(PanelLevel.Pane, panel.Level);
        Assert.Equal("a", panel.Rows.Selected!.Key);
        panel.Handle(MenuInputAction.Back);
        Assert.Equal(PanelLevel.Settlements, panel.Level);
        Assert.False(panel.IsFinished);
        panel.Handle(MenuInputAction.Back);

        Assert.True(panel.IsFinished);
    }

    [Fact]
    public void An_embedded_panel_asks_its_host_to_exit_instead_of_finishing()
    {
        var (panel, _) = Make(embedded: true);
        var exits = 0;
        panel.ExitRequested += () => exits++;

        panel.Handle(MenuInputAction.Back);

        Assert.Equal(1, exits);
        Assert.False(panel.IsFinished);
    }

    [Fact]
    public void Opening_for_one_settlement_starts_in_its_pane_and_back_closes_from_there()
    {
        var (panel, _) = Make(start: "s2");

        Assert.Equal(PanelLevel.Pane, panel.Level);
        Assert.Equal("Hill - Overview", panel.Header);

        panel.Handle(MenuInputAction.Back);

        Assert.True(panel.IsFinished);
    }

    [Fact]
    public void Finishing_stops_listening_to_provider_changes()
    {
        var (panel, provider) = Make();
        panel.Handle(MenuInputAction.Back);
        var raised = 0;
        panel.Changed += () => raised++;

        provider.Raise();

        Assert.Equal(0, raised);
    }
}
