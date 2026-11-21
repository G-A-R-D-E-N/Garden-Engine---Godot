using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WorkshopBuildTests
{
    static (WorkshopBuildController Ctrl, FakeCatalog Catalog, FakeWorld World, List<WorkshopIntent> Intents) Make(
        string? last = null, FakeSettlements? settlers = null, WorkshopSettlement? strip = null)
    {
        var catalog = FakeCatalog.Sample();
        var world = new FakeWorld { Settlement = strip };
        var ctrl = new WorkshopBuildController(catalog, world, settlers, last);
        return (ctrl, catalog, world, WorkshopTestHelpers.Capture(ctrl));
    }

    static void OpenFurniture(WorkshopBuildController ctrl)
    {
        Assert.True(ctrl.Handle(MenuInputAction.Accept));
        Assert.Equal("Build > Furniture", ctrl.Header);
    }

    static WorkshopSettlement Strip(double size, int people = 3) => new()
    {
        BuildSize = size,
        Values = new Dictionary<SettlementStat, int> { [SettlementStat.People] = people },
    };

    [Fact]
    public void The_root_lists_categories_with_counts_and_a_category_opens_with_a_breadcrumb()
    {
        var (ctrl, _, _, _) = Make();

        Assert.Equal(new[] { "Furniture (2)", "Empty (0)" }, WorkshopTestHelpers.RowTexts(ctrl.List));
        Assert.Equal("Build", ctrl.Header);

        OpenFurniture(ctrl);

        Assert.Equal(new[] { "Chair x 3", "Bed - Lacking materials" }, WorkshopTestHelpers.RowTexts(ctrl.List));
        Assert.Equal("Furniture", ctrl.Path.Single().Name);
    }

    [Fact]
    public void An_empty_category_says_so_instead_of_showing_a_blank_pane()
    {
        var (ctrl, _, _, _) = Make();
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(0, ctrl.List.RowCount);
        Assert.Equal(WorkshopBuildController.EmptyText, ctrl.DetailLines[0].Text);
    }

    [Fact]
    public void Accept_on_a_buildable_item_asks_for_one_ghost_and_not_a_second_for_the_same_item()
    {
        var (ctrl, _, _, intents) = Make();
        OpenFurniture(ctrl);

        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new GhostRequested("chair"), intents.Single());
    }

    [Fact]
    public void Moving_to_an_item_that_cannot_be_built_clears_the_ghost()
    {
        var (ctrl, _, world, intents) = Make();
        OpenFurniture(ctrl);
        ctrl.Handle(MenuInputAction.Accept);
        world.Aim = WorkshopAim.Ghost;

        ctrl.Handle(MenuInputAction.Down);

        Assert.Equal(new WorkshopIntent[] { new GhostRequested("chair"), new GhostCleared() }, intents);
    }

    [Fact]
    public void Aiming_away_from_the_ghost_clears_it_and_tells_the_world()
    {
        var (ctrl, _, world, intents) = Make();
        OpenFurniture(ctrl);
        ctrl.Handle(MenuInputAction.Accept);
        world.Aim = WorkshopAim.Ghost;
        ctrl.Tick(0.016);
        world.Aim = WorkshopAim.Browsing;

        ctrl.Tick(0.016);

        Assert.Equal(new WorkshopIntent[] { new GhostRequested("chair"), new GhostCleared() }, intents);
    }

    [Fact]
    public void Accept_on_a_lacking_item_says_why_and_sends_no_ghost()
    {
        var (ctrl, _, _, intents) = Make();
        OpenFurniture(ctrl);
        ctrl.Handle(MenuInputAction.Down);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("You don't have the materials for that.", ctrl.Message);
        Assert.Empty(intents);
    }

    [Fact]
    public void Back_with_a_ghost_clears_it_and_goes_up_and_never_stores_or_scraps()
    {
        var (ctrl, _, world, intents) = Make();
        OpenFurniture(ctrl);
        ctrl.Handle(MenuInputAction.Accept);
        world.Aim = WorkshopAim.Ghost;

        ctrl.Handle(MenuInputAction.Back);

        Assert.Equal("Build", ctrl.Header);
        Assert.False(ctrl.IsFinished);
        Assert.Equal(2, intents.Count);
        Assert.IsType<GhostCleared>(intents[1]);
        Assert.DoesNotContain(intents, intent => intent is StoreRequested or ScrapRequested);
    }

    [Fact]
    public void Placing_is_refused_with_the_reason_until_the_spot_is_valid()
    {
        var (ctrl, _, world, intents) = Make();
        world.Aim = WorkshopAim.Ghost;
        world.Placement = PlacementStatus.Blocked;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(WorkshopBuildController.PlacementText(PlacementStatus.Blocked), ctrl.Message);
        Assert.DoesNotContain(intents, intent => intent is PlaceRequested);
        Assert.Contains(WorkshopBuildController.PlacementText(PlacementStatus.Blocked), WorkshopTestHelpers.Texts(ctrl));

        world.Placement = PlacementStatus.Valid;
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Single(intents.OfType<PlaceRequested>());
    }

    [Fact]
    public void Store_scrap_and_repair_open_an_inline_panel_and_only_accept_sends_the_intent()
    {
        var (ctrl, _, world, intents) = Make();
        world.Aim = WorkshopAim.TargetObject;
        world.Target = new WorkshopTarget("t1", "Lamp")
        {
            Storable = true,
            Scrappable = true,
            Repairable = true,
            RepairAffordable = true,
            Yield = new[] { new WorkshopMaterial("steel", "Steel", 2) },
        };

        ctrl.Handle(MenuInputAction.Favourite);
        Assert.True(ctrl.IsConfirming);
        ctrl.Handle(MenuInputAction.Back);
        Assert.False(ctrl.IsConfirming);
        Assert.Empty(intents);

        ctrl.Handle(MenuInputAction.Compare);
        Assert.Contains("Steel x 2", WorkshopTestHelpers.Texts(ctrl));
        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(new ScrapRequested("t1"), intents.Single());

        ctrl.Handle(MenuInputAction.Search);
        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(new RepairRequested("t1"), intents.Last());
        Assert.False(ctrl.IsConfirming);
    }

    [Fact]
    public void A_repair_that_cannot_be_afforded_shows_information_with_only_a_close_hint()
    {
        var (ctrl, _, world, intents) = Make();
        world.Aim = WorkshopAim.TargetObject;
        world.Target = new WorkshopTarget("t1", "Lamp") { Repairable = true };

        ctrl.Handle(MenuInputAction.Search);

        Assert.True(ctrl.IsConfirming);
        Assert.Equal(new[] { MenuInputAction.Back }, ctrl.Hints.Select(hint => hint.Action).ToArray());
        ctrl.Handle(MenuInputAction.Accept);
        Assert.Empty(intents);
    }

    [Fact]
    public void Aiming_at_something_else_closes_an_open_panel_without_an_intent()
    {
        var (ctrl, _, world, intents) = Make();
        world.Aim = WorkshopAim.TargetObject;
        world.Target = new WorkshopTarget("t1", "Lamp") { Scrappable = true };
        ctrl.Handle(MenuInputAction.Compare);
        Assert.True(ctrl.IsConfirming);

        world.Target = new WorkshopTarget("t2", "Crate") { Scrappable = true };
        ctrl.Tick(0.1);

        Assert.False(ctrl.IsConfirming);
        Assert.Empty(intents);
    }

    [Fact]
    public void A_blocked_scrap_says_so_and_opens_no_panel()
    {
        var (ctrl, _, world, _) = Make();
        world.Aim = WorkshopAim.TargetObject;
        world.Target = new WorkshopTarget("t1", "Lamp") { Scrappable = true, ScrapBlocked = true };

        ctrl.Handle(MenuInputAction.Compare);

        Assert.False(ctrl.IsConfirming);
        Assert.Equal("This can't be scrapped right now.", ctrl.Message);
    }

    [Fact]
    public void The_build_limit_message_is_said_once_when_the_meter_reaches_full()
    {
        var (ctrl, _, world, _) = Make(strip: Strip(0.5));

        ctrl.Tick(0.1);
        Assert.Equal(string.Empty, ctrl.Message);

        world.Settlement = Strip(1.0);
        ctrl.Tick(0.1);
        Assert.Equal(WorkshopBuildController.LimitText, ctrl.Message);

        ctrl.Tick(ListDetailController<WorkshopNode>.MessageSeconds + 0.1);
        Assert.Equal(string.Empty, ctrl.Message);
        ctrl.Tick(0.1);
        Assert.Equal(string.Empty, ctrl.Message);

        world.Settlement = Strip(0.5);
        ctrl.Tick(0.1);
        world.Settlement = Strip(1.0);
        ctrl.Tick(0.1);
        Assert.Equal(WorkshopBuildController.LimitText, ctrl.Message);
    }

    [Fact]
    public void A_menu_that_opens_on_a_full_settlement_does_not_announce_the_limit()
    {
        var (ctrl, _, _, _) = Make(strip: Strip(1.0));

        ctrl.Tick(0.1);

        Assert.Equal(string.Empty, ctrl.Message);
    }

    [Fact]
    public void Leaving_the_build_area_counts_five_to_one_then_closes_once()
    {
        var (ctrl, _, world, _) = Make();
        var finished = 0;
        ctrl.Finished += _ => finished++;
        world.OutsideBuildArea = true;

        ctrl.Tick(0.1);
        var seen = new List<string> { ctrl.Message };
        for (var i = 0; i < 4; i++)
        {
            ctrl.Tick(1.0);
            seen.Add(ctrl.Message);
        }

        Assert.Equal(
            new[]
            {
                "Build mode closing in 5 s", "Build mode closing in 4 s", "Build mode closing in 3 s",
                "Build mode closing in 2 s", "Build mode closing in 1 s",
            },
            seen);
        Assert.False(ctrl.IsFinished);

        ctrl.Tick(1.0);
        ctrl.Tick(1.0);

        Assert.True(ctrl.IsFinished);
        Assert.Equal(1, finished);
    }

    [Fact]
    public void Coming_back_inside_cancels_the_countdown()
    {
        var (ctrl, _, world, _) = Make();
        world.OutsideBuildArea = true;
        ctrl.Tick(0.1);
        ctrl.Tick(1.0);

        world.OutsideBuildArea = false;
        ctrl.Tick(0.1);

        Assert.Equal("Build mode stays open", ctrl.Message);
        world.OutsideBuildArea = true;
        ctrl.Tick(0.1);
        Assert.Equal("Build mode closing in 5 s", ctrl.Message);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void Exit_cancels_a_held_object_then_finishes_once()
    {
        var (ctrl, _, world, intents) = Make();
        world.Aim = WorkshopAim.Holding;
        var finished = 0;
        ctrl.Finished += _ => finished++;

        ctrl.Handle(MenuInputAction.Sort);
        ctrl.Exit();

        Assert.Equal(new WorkshopIntent[] { new CancelRequested() }, intents);
        Assert.Equal(1, finished);
        Assert.False(ctrl.IsConfirmed);
    }

    [Fact]
    public void Exit_discards_an_unplaced_ghost()
    {
        var (ctrl, _, world, intents) = Make();
        OpenFurniture(ctrl);
        ctrl.Handle(MenuInputAction.Accept);
        world.Aim = WorkshopAim.Ghost;

        ctrl.Handle(MenuInputAction.Sort);

        Assert.True(ctrl.IsFinished);
        Assert.IsType<GhostCleared>(intents.Last());
    }

    [Fact]
    public void Back_at_the_root_exits_and_back_in_a_category_only_goes_up()
    {
        var (ctrl, _, _, _) = Make();
        OpenFurniture(ctrl);

        ctrl.Handle(MenuInputAction.Back);
        Assert.False(ctrl.IsFinished);
        Assert.Equal("furn", ctrl.LastNodeId);

        ctrl.Handle(MenuInputAction.Back);
        Assert.True(ctrl.IsFinished);
    }

    [Fact]
    public void The_last_node_is_remembered_and_reopened_with_its_path_and_an_unknown_one_is_ignored()
    {
        var (ctrl, _, _, _) = Make("bed");

        Assert.Equal("Build > Furniture", ctrl.Header);
        Assert.Equal("bed", ctrl.LastNodeId);

        var (fresh, _, _, _) = Make("nothing");

        Assert.Equal("Build", fresh.Header);
    }

    [Fact]
    public void Detail_shows_materials_with_have_and_need_and_marks_the_short_ones()
    {
        var (ctrl, _, _, _) = Make();
        OpenFurniture(ctrl);

        var texts = WorkshopTestHelpers.Texts(ctrl);

        Assert.Contains("Wood 7/5", texts);
        ctrl.Handle(MenuInputAction.Down);
        Assert.Contains("Wood 7/10 short", WorkshopTestHelpers.Texts(ctrl));
        Assert.Contains("Status: Lacking materials", WorkshopTestHelpers.Texts(ctrl));
    }

    [Fact]
    public void Free_build_shows_unlimited_materials_and_a_free_tag()
    {
        var (ctrl, catalog, _, _) = Make();
        catalog.FreeBuild = true;
        ctrl.Refresh();
        OpenFurniture(ctrl);

        Assert.Equal("Chair free", ctrl.List.RowText(0));
        Assert.Contains("Wood Unlimited/5", WorkshopTestHelpers.Texts(ctrl));
    }

    [Fact]
    public void Track_materials_toggles_only_when_something_is_short()
    {
        var (ctrl, _, world, intents) = Make();
        OpenFurniture(ctrl);
        world.Aim = WorkshopAim.Ghost;

        ctrl.Handle(MenuInputAction.TabPrev);
        Assert.Equal("Nothing is short.", ctrl.Message);

        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.TabPrev);
        ctrl.Handle(MenuInputAction.TabPrev);

        Assert.Equal(new[] { true, false }, intents.OfType<TrackRequested>().Select(track => track.Tracked).ToArray());
    }

    [Fact]
    public void The_settlement_strip_clamps_values_shows_missing_ones_and_warns_when_nearly_full()
    {
        var strip = new WorkshopSettlement
        {
            Values = new Dictionary<SettlementStat, int>
            {
                [SettlementStat.People] = 1500,
                [SettlementStat.Food] = -4,
                [SettlementStat.Happiness] = 55,
            },
            Alerts = new HashSet<SettlementStat> { SettlementStat.Food },
            HappinessTrend = -1,
            BuildSize = 0.92,
        };
        var (ctrl, _, _, _) = Make(strip: strip);

        var texts = WorkshopTestHelpers.Texts(ctrl);

        Assert.Contains("People: 999", texts);
        Assert.Contains("Food: 0 alert", texts);
        Assert.Contains("Water: --", texts);
        Assert.Contains("Happiness: 55 falling", texts);
        Assert.Contains("Build size: 92% Nearly full", texts);
    }

    [Fact]
    public void A_hidden_readout_draws_no_strip()
    {
        var (ctrl, _, _, _) = Make(strip: new WorkshopSettlement { ReadoutVisible = false });

        Assert.DoesNotContain("Settlement", WorkshopTestHelpers.Texts(ctrl));
    }

    [Fact]
    public void A_settler_move_opens_a_picker_without_the_home_settlement_and_says_why_when_refused()
    {
        var settlers = new FakeSettlements();
        var (ctrl, _, world, _) = Make(settlers: settlers);
        DestinationPickerController? picker = null;
        ctrl.PickerRequested += p => picker = p;
        world.Aim = WorkshopAim.TargetSettler;
        world.Settler = new WorkshopSettlerCard("a", "Ann") { HomeId = "s1", CanMove = true };

        ctrl.Handle(MenuInputAction.Compare);

        Assert.NotNull(picker);
        Assert.Equal(new[] { "Hill" }, WorkshopTestHelpers.RowTexts(picker!.List));

        picker = null;
        world.Settler = new WorkshopSettlerCard("a", "Ann") { HomeId = "s1", MoveReason = "Ann is busy." };
        ctrl.Handle(MenuInputAction.Compare);

        Assert.Null(picker);
        Assert.Equal("Ann is busy.", ctrl.Message);
    }

    [Fact]
    public void A_settler_move_without_a_provider_is_not_available()
    {
        var (ctrl, _, world, _) = Make();
        world.Aim = WorkshopAim.TargetSettler;
        world.Settler = new WorkshopSettlerCard("a", "Ann") { CanMove = true };

        ctrl.Handle(MenuInputAction.Compare);

        Assert.Equal("Not available yet.", ctrl.Message);
    }

    [Fact]
    public void Commanding_a_settler_sends_the_intent_or_says_why_not()
    {
        var (ctrl, _, world, intents) = Make();
        world.Aim = WorkshopAim.TargetSettler;
        world.Settler = new WorkshopSettlerCard("a", "Ann");

        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal("Ann won't take orders from you.", ctrl.Message);

        world.Settler = new WorkshopSettlerCard("a", "Ann") { CanCommand = true };
        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(new SettlerCommandRequested("a"), intents.Single());
    }

    [Fact]
    public void Hints_follow_the_aim_and_mark_a_blocked_placement()
    {
        var (ctrl, _, world, _) = Make();
        world.Aim = WorkshopAim.Ghost;
        world.Placement = PlacementStatus.Blocked;

        Assert.Equal("Place (blocked)", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Accept).Label);

        world.Placement = PlacementStatus.Valid;
        Assert.Equal("Place", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Accept).Label);

        world.Aim = WorkshopAim.Holding;
        Assert.Equal("Put down", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Accept).Label);
    }

    [Fact]
    public void Changes_in_the_world_raise_the_changed_event_on_tick()
    {
        var (ctrl, _, world, _) = Make();
        var raised = 0;
        ctrl.Changed += () => raised++;

        ctrl.Tick(0.1);
        Assert.Equal(0, raised);

        world.Placement = PlacementStatus.Blocked;
        ctrl.Tick(0.1);
        Assert.Equal(1, raised);
    }
}
