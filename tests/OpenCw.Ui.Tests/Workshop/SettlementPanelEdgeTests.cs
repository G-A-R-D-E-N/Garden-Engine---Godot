using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettlementPanelEdgeTests
{
    static (SettlementPanelController Panel, FakeSettlements Provider) Make(string? start = null)
    {
        var provider = new FakeSettlements();
        return (new SettlementPanelController(provider, false, start), provider);
    }

    static void Press(SettlementPanelController panel, params MenuInputAction[] actions)
    {
        foreach (var action in actions) panel.Handle(action);
    }

    static (string, LineTone)[] Lines(SettlementPanelController panel) => panel.DetailLines.Select(line => (line.Text, line.Tone)).ToArray();

    static readonly MenuInputAction[] ToSettlers = [MenuInputAction.Accept, MenuInputAction.TabNext];

    static readonly MenuInputAction[] ToRoutes = [MenuInputAction.Accept, MenuInputAction.TabNext, MenuInputAction.TabNext];

    [Theory]
    [InlineData(SettlerJob.Unassigned, "Unassigned")]
    [InlineData(SettlerJob.Food, "Farming")]
    [InlineData(SettlerJob.Defense, "Guarding")]
    [InlineData(SettlerJob.Provisioner, "Provisioner")]
    [InlineData(SettlerJob.Scavenger, "Scavenging")]
    [InlineData(SettlerJob.Vendor, "Vending")]
    [InlineData(SettlerJob.Other, "Working")]
    [InlineData((SettlerJob)99, "Unassigned")]
    public void Every_job_has_its_word(SettlerJob job, string expected)
    {
        Assert.Equal(expected, SettlementPanelController.JobWord(job));
    }

    [Theory]
    [InlineData(2.346, "Food: 2.35")]
    [InlineData(2.0, "Food: 2")]
    [InlineData(0.0, "Food: 0")]
    [InlineData(-1.5, "Food: -1.5")]
    [InlineData(1234.5, "Food: 1234.5")]
    public void A_metric_shows_up_to_two_decimals(double value, string expected)
    {
        Assert.Equal(expected, SettlementPanelController.MetricText(new SettlementMetric("Food", value, 0)));
    }

    [Fact]
    public void A_settlement_card_lists_its_metrics_toned_by_rating_and_flags_an_attack()
    {
        var (panel, _) = Make();
        Assert.Equal(new[] { ("Sanctuary", LineTone.Heading), ("Food: 12.5", LineTone.Good), ("Water: --", LineTone.Normal) }, Lines(panel));

        panel.Handle(MenuInputAction.Down);

        Assert.Equal(new[] { ("Hill", LineTone.Heading), ("Under attack", LineTone.Bad), ("Food: 3", LineTone.Bad) }, Lines(panel));
    }

    [Fact]
    public void The_overview_pane_describes_the_selected_metric_and_an_attack()
    {
        var (panel, _) = Make();
        panel.Handle(MenuInputAction.Accept);
        Assert.Equal(new[] { ("Food: 12.5", LineTone.Good) }, Lines(panel));

        panel.Handle(MenuInputAction.Down);
        Assert.Equal(new[] { ("Water: --", LineTone.Normal) }, Lines(panel));

        var (hill, _) = Make("s2");
        Assert.Equal(new[] { ("Under attack", LineTone.Bad), ("Food: 3", LineTone.Bad) }, Lines(hill));
    }

    [Fact]
    public void Accepting_on_the_overview_does_nothing()
    {
        var (panel, _) = Make();
        panel.Handle(MenuInputAction.Accept);

        panel.Handle(MenuInputAction.Accept);

        Assert.Equal(PanelLevel.Pane, panel.Level);
        Assert.Equal(SettlementTab.Overview, panel.Tab);
    }

    [Fact]
    public void The_hints_follow_the_level_the_tab_and_whether_back_leaves_the_panel()
    {
        var (panel, _) = Make();
        var settlements = panel.Hints.ToArray();
        panel.Handle(MenuInputAction.Accept);
        var overview = panel.Hints.ToArray();
        panel.Handle(MenuInputAction.TabNext);
        var settlers = panel.Hints.ToArray();
        panel.Handle(MenuInputAction.Accept);
        var actions = panel.Hints.ToArray();
        var (hill, _) = Make("s2");

        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Open"), new MenuHint(MenuInputAction.Back, "Close") }, settlements);
        Assert.Equal(
            new[]
            {
                new MenuHint(MenuInputAction.Accept, "Select"),
                new MenuHint(MenuInputAction.TabPrev, "Previous tab"),
                new MenuHint(MenuInputAction.TabNext, "Next tab"),
                new MenuHint(MenuInputAction.Back, "Back"),
            },
            overview);
        Assert.Equal(new MenuHint(MenuInputAction.Accept, "Actions"), settlers[0]);
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Do it"), new MenuHint(MenuInputAction.Back, "Back") }, actions);
        Assert.Equal(new MenuHint(MenuInputAction.Back, "Close"), hill.Hints[3]);
    }

    [Fact]
    public void Tabs_are_ignored_outside_the_pane()
    {
        var (panel, _) = Make();
        Assert.False(panel.Handle(MenuInputAction.TabNext));
        Assert.False(panel.Handle(MenuInputAction.Left));

        Press(panel, ToSettlers);
        panel.Handle(MenuInputAction.Accept);

        Assert.Equal(PanelLevel.Actions, panel.Level);
        Assert.False(panel.Handle(MenuInputAction.TabPrev));
        Assert.False(panel.Handle(MenuInputAction.Right));
        Assert.Equal(SettlementTab.Settlers, panel.Tab);
    }

    [Fact]
    public void The_header_names_the_settler_or_the_route_being_acted_on()
    {
        var (panel, _) = Make();
        Assert.Equal(string.Empty, panel.Header);
        Press(panel, ToSettlers);
        panel.Handle(MenuInputAction.Accept);
        Assert.Equal("Sanctuary - Ann", panel.Header);

        var (routes, _) = Make();
        Press(routes, ToRoutes);
        routes.Handle(MenuInputAction.Accept);

        Assert.Equal("Sanctuary - Sanctuary to Hill", routes.Header);
    }

    [Fact]
    public void The_routes_tab_lists_the_routes_ending_at_the_settlement_either_way_round()
    {
        var (panel, provider) = Make();
        provider.RouteList.Add(new RouteInfo("r2", "s2", "s1", "b"));
        provider.RouteList.Add(new RouteInfo("r3", "x", "y", "b"));
        provider.RouteList.Add(new RouteInfo("r4", "s1", "ghost", "b"));

        Press(panel, ToRoutes);

        Assert.Equal(new[] { "Sanctuary to Hill", "Hill to Sanctuary", "Sanctuary to ghost" }, WorkshopTestHelpers.RowTexts(panel.List));
    }

    [Fact]
    public void A_route_card_names_the_runner_when_there_is_one()
    {
        var (panel, provider) = Make();
        Press(panel, ToRoutes);
        Assert.Equal(new[] { ("Sanctuary to Hill", LineTone.Heading), ("Runs: Ann", LineTone.Normal) }, Lines(panel));

        provider.RouteList[0] = provider.RouteList[0] with { SettlerId = "nobody" };
        provider.Raise();

        Assert.Equal(new[] { ("Sanctuary to Hill", LineTone.Heading) }, Lines(panel));
    }

    [Fact]
    public void A_settler_card_shows_home_job_work_object_and_a_route_that_still_exists()
    {
        var (panel, provider) = Make();
        Press(panel, ToSettlers);
        Assert.Equal(
            new[]
            {
                ("Ann", LineTone.Heading),
                ("Home: Sanctuary", LineTone.Normal),
                ("Job: Farming", LineTone.Normal),
                ("Works at: Crop", LineTone.Normal),
            },
            Lines(panel));

        provider.SettlerList[0] = provider.SettlerList[0] with { RouteId = "r1" };
        provider.Raise();
        Assert.Equal(("Route: Sanctuary to Hill", LineTone.Normal), Lines(panel).Last());

        provider.SettlerList[0] = provider.SettlerList[0] with { RouteId = "gone" };
        provider.Raise();
        Assert.Equal(("Works at: Crop", LineTone.Normal), Lines(panel).Last());
    }

    [Fact]
    public void A_settler_on_a_route_can_be_unassigned_even_with_no_job()
    {
        var (panel, provider) = Make();
        provider.SettlerList[1] = provider.SettlerList[1] with { RouteId = "r1" };
        Press(panel, MenuInputAction.Accept, MenuInputAction.TabNext, MenuInputAction.Down, MenuInputAction.Accept);
        Press(panel, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Accept);

        Assert.True(panel.IsConfirming);
        Assert.Equal(new[] { ("Take Bob off their current task?", LineTone.Heading) }, Lines(panel));
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Confirm"), new MenuHint(MenuInputAction.Back, "Cancel") }, panel.Hints);
    }

    [Fact]
    public void Other_input_while_confirming_is_swallowed()
    {
        var (panel, provider) = Make();
        Press(panel, ToRoutes);
        Press(panel, MenuInputAction.Accept, MenuInputAction.Accept);
        Assert.Equal(new[] { ("Remove the route Sanctuary to Hill?", LineTone.Heading) }, Lines(panel));

        Assert.True(panel.Handle(MenuInputAction.Down));

        Assert.True(panel.IsConfirming);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public void A_failed_unassign_with_no_reason_says_it_failed()
    {
        var (panel, provider) = Make();
        provider.Next = new ActionResult(false);
        Press(panel, MenuInputAction.Accept, MenuInputAction.TabNext, MenuInputAction.Accept);
        Press(panel, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Accept, MenuInputAction.Accept);

        Assert.Equal(DestinationPickerController.FailedText, panel.Message);
        Assert.Equal(new[] { "unassign:a" }, provider.Calls);
    }

    [Fact]
    public void A_disabled_action_without_a_reason_says_not_available_in_the_card_and_on_accept()
    {
        var (panel, provider) = Make();
        provider.SettlerList[0] = provider.SettlerList[0] with { CanMove = false };
        Press(panel, ToSettlers);
        panel.Handle(MenuInputAction.Accept);

        Assert.Equal((DestinationPickerController.NotAvailableText, LineTone.Bad), Lines(panel).Last());

        panel.Handle(MenuInputAction.Accept);

        Assert.Equal(DestinationPickerController.NotAvailableText, panel.Message);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public void A_settlement_that_vanishes_closes_a_panel_opened_on_it_and_returns_a_full_panel_to_the_list()
    {
        var (hill, hillProvider) = Make("s2");
        hillProvider.SettlementList.RemoveAt(1);
        hillProvider.Raise();
        Assert.True(hill.IsFinished);

        var (panel, provider) = Make();
        panel.Handle(MenuInputAction.Accept);
        provider.SettlementList.RemoveAt(0);
        provider.Raise();

        Assert.False(panel.IsFinished);
        Assert.Equal(PanelLevel.Settlements, panel.Level);
        Assert.Equal(new[] { "Hill - Under attack" }, WorkshopTestHelpers.RowTexts(panel.List));
    }

    [Fact]
    public void An_unknown_start_settlement_opens_the_list()
    {
        var (panel, _) = Make("nope");

        Assert.Equal(PanelLevel.Settlements, panel.Level);
    }

    [Fact]
    public void The_panel_needs_its_provider()
    {
        Assert.Throws<System.ArgumentNullException>(() => new SettlementPanelController(null!));
    }
}
