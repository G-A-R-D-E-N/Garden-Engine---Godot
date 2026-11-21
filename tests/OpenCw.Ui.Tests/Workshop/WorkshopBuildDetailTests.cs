using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WorkshopBuildDetailTests
{
    static (WorkshopBuildController Ctrl, FakeCatalog Catalog, FakeWorld World) Make(FakeCatalog? catalog = null, FakeWorld? world = null)
    {
        catalog ??= FakeCatalog.Sample();
        world ??= new FakeWorld();
        return (new WorkshopBuildController(catalog, world), catalog, world);
    }

    static (WorkshopBuildController Ctrl, FakeCatalog Catalog, FakeWorld World) Single(WorkshopItem item)
    {
        var catalog = new FakeCatalog { Roots = [WorkshopNode.Leaf(item)] };
        return Make(catalog);
    }

    static (string, LineTone)[] Lines(WorkshopBuildController ctrl) => ctrl.DetailLines.Select(line => (line.Text, line.Tone)).ToArray();

    static MenuHint[] HintsOf(WorkshopBuildController ctrl) => ctrl.Hints.ToArray();

    static readonly WorkshopTarget Crate = new("o1", "Crate")
    {
        Storable = true,
        Scrappable = true,
        Repairable = true,
        RepairAffordable = true,
        Yield = [new WorkshopMaterial("wood", "Wood", 2)],
        RepairCost = [new WorkshopMaterial("steel", "Steel", 4)],
    };

    [Theory]
    [InlineData(WorkshopItemStatus.Available, "")]
    [InlineData(WorkshopItemStatus.LackingMaterials, "Lacking materials")]
    [InlineData(WorkshopItemStatus.PerkRequired, "Perk required")]
    [InlineData(WorkshopItemStatus.BudgetFull, "Budget full")]
    public void Every_item_status_has_its_word_and_available_has_none(WorkshopItemStatus status, string expected)
    {
        Assert.Equal(expected, WorkshopBuildController.StatusWord(status));
    }

    [Theory]
    [InlineData(PlacementStatus.Valid, "")]
    [InlineData(PlacementStatus.OutsideArea, "This spot is outside your build area.")]
    [InlineData(PlacementStatus.Blocked, "Something blocks this spot.")]
    [InlineData(PlacementStatus.Unsupported, "This needs something to rest on.")]
    [InlineData(PlacementStatus.NeedsTerrain, "This only fits on open ground.")]
    [InlineData(PlacementStatus.NeedsWater, "This only fits in water.")]
    [InlineData(PlacementStatus.WireTooLong, "That run is too long.")]
    [InlineData(PlacementStatus.WireDuplicate, "These two are already linked.")]
    [InlineData(PlacementStatus.BudgetFull, "Build limit reached.")]
    [InlineData((PlacementStatus)99, "")]
    public void Every_placement_status_has_its_sentence(PlacementStatus status, string expected)
    {
        Assert.Equal(expected, WorkshopBuildController.PlacementText(status));
    }

    [Fact]
    public void A_row_shows_the_buildable_count_free_new_and_status_tags_in_that_order()
    {
        var catalog = new FakeCatalog
        {
            Roots =
            [
                WorkshopNode.Leaf(new WorkshopItem("a", "Chair")),
                WorkshopNode.Leaf(new WorkshopItem("b", "Table")),
                WorkshopNode.Leaf(new WorkshopItem("c", "Lamp") { IsNew = true }),
                WorkshopNode.Leaf(new WorkshopItem("d", "Bed") { IsNew = true }),
            ],
        };
        catalog.Buildable["b"] = 3;
        catalog.Buildable["c"] = 12;
        catalog.Status["a"] = WorkshopItemStatus.LackingMaterials;
        catalog.Status["d"] = WorkshopItemStatus.PerkRequired;

        var (ctrl, _, _) = Make(catalog);

        Assert.Equal(
            new[] { "Chair - Lacking materials", "Table x 3", "Lamp x 12 NEW", "Bed NEW - Perk required" },
            WorkshopTestHelpers.RowTexts(ctrl.List));

        catalog.FreeBuild = true;
        Assert.Equal(new[] { "Chair free - Lacking materials", "Table free", "Lamp free NEW", "Bed free NEW - Perk required" },
            WorkshopTestHelpers.RowTexts(ctrl.List));
    }

    [Fact]
    public void A_category_counts_only_its_valid_entries_and_invalid_nodes_are_not_listed()
    {
        var catalog = new FakeCatalog
        {
            Roots =
            [
                WorkshopNode.Category(
                    "cat", "Cat", WorkshopNode.Leaf(new WorkshopItem("a", "Chair")), new WorkshopNode(" ", "NoId"), new WorkshopNode("x", "  ")),
                new WorkshopNode("", "Blank"),
            ],
        };

        var (ctrl, _, _) = Make(catalog);

        Assert.Equal(new[] { "Cat (1)" }, WorkshopTestHelpers.RowTexts(ctrl.List));
        Assert.Equal(new[] { ("Cat", LineTone.Heading), ("1 entries", LineTone.Muted) }, Lines(ctrl));
    }

    [Fact]
    public void An_item_card_lists_needs_power_worker_outputs_perks_and_stored_in_order()
    {
        var item = new WorkshopItem("gen", "Gen")
        {
            Description = "Makes power.",
            Materials = [new WorkshopMaterial("steel", "Steel", 3)],
            PowerRequired = 2,
            RequiresWorker = true,
            Produces = new WorkshopOutput(Food: 0, Water: 4, Power: 5, Safety: -2, Happiness: 1),
            Perks = [new WorkshopPerk("Science", 2, true), new WorkshopPerk("Armorer", 1, false)],
            Stored = 3,
        };
        var (ctrl, catalog, _) = Single(item);
        catalog.Stock["steel"] = 3;

        Assert.Equal(
            new[]
            {
                ("Gen", LineTone.Heading),
                ("Makes power.", LineTone.Normal),
                ("Needs", LineTone.Heading),
                ("Steel 3/3", LineTone.Normal),
                (WorkshopBuildController.PoolText, LineTone.Muted),
                ("Power: 2", LineTone.Normal),
                ("Needs a worker", LineTone.Normal),
                ("Produces", LineTone.Heading),
                ("Water 4", LineTone.Normal),
                ("Power 5", LineTone.Normal),
                ("Reduces defense", LineTone.Normal),
                ("Happiness 1", LineTone.Normal),
                ("Perks", LineTone.Heading),
                ("Science, rank 2 - met", LineTone.Good),
                ("Armorer, rank 1 - locked", LineTone.Bad),
                ("Stored: 3", LineTone.Normal),
            },
            Lines(ctrl));
    }

    [Fact]
    public void Positive_safety_reads_as_defense_and_an_item_with_no_outputs_has_no_produces_heading()
    {
        var (defense, _, _) = Single(new WorkshopItem("t", "Turret") { Produces = new WorkshopOutput(Safety: 3) });
        var (plain, _, _) = Single(new WorkshopItem("p", "Plank"));

        Assert.Equal(new[] { ("Turret", LineTone.Heading), ("Produces", LineTone.Heading), ("Defense 3", LineTone.Normal) }, Lines(defense));
        Assert.Equal(new[] { ("Plank", LineTone.Heading) }, Lines(plain));
    }

    [Fact]
    public void An_item_needing_only_power_has_a_needs_heading_but_no_pool_note()
    {
        var (ctrl, _, _) = Single(new WorkshopItem("l", "Lamp") { PowerRequired = 1 });

        Assert.Equal(new[] { ("Lamp", LineTone.Heading), ("Needs", LineTone.Heading), ("Power: 1", LineTone.Normal) }, Lines(ctrl));
    }

    [Fact]
    public void An_item_that_cannot_be_built_leads_with_its_status_line()
    {
        var (ctrl, catalog, _) = Single(new WorkshopItem("l", "Lamp") { Description = "Bright." });
        catalog.Status["l"] = WorkshopItemStatus.BudgetFull;

        Assert.Equal(new[] { ("Lamp", LineTone.Heading), ("Status: Budget full", LineTone.Bad), ("Bright.", LineTone.Normal) }, Lines(ctrl));
    }

    [Theory]
    [InlineData(WorkshopItemStatus.PerkRequired, "You need a perk to build that.")]
    [InlineData(WorkshopItemStatus.BudgetFull, "Build limit reached.")]
    [InlineData(WorkshopItemStatus.LackingMaterials, "You don't have the materials for that.")]
    public void Choosing_an_item_that_cannot_be_built_says_why(WorkshopItemStatus status, string sentence)
    {
        var (ctrl, catalog, _) = Single(new WorkshopItem("l", "Lamp"));
        catalog.Status["l"] = status;
        var intents = WorkshopTestHelpers.Capture(ctrl);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(sentence, ctrl.Message);
        Assert.Empty(intents);
    }

    [Fact]
    public void An_object_card_lists_what_can_be_done_and_what_is_blocked_right_now()
    {
        var target = new WorkshopTarget("o1", "Crate")
        {
            Selectable = true,
            Storable = true,
            Scrappable = true,
            ScrapBlocked = true,
            Repairable = true,
            RepairBlocked = true,
            PowerConnectable = true,
        };
        var (ctrl, _, world) = Make();
        world.Aim = WorkshopAim.TargetObject;
        world.Target = target;

        Assert.Equal(
            new[]
            {
                ("Crate", LineTone.Heading),
                ("Can be moved", LineTone.Normal),
                ("Can be stored", LineTone.Normal),
                ("Can't be scrapped right now", LineTone.Bad),
                ("Can't be repaired right now", LineTone.Bad),
                ("Can take a wire", LineTone.Normal),
            },
            Lines(ctrl));

        world.Target = target with { ScrapBlocked = false, RepairBlocked = false };

        Assert.Equal(new[] { ("Can be scrapped", LineTone.Normal), ("Can be repaired", LineTone.Normal) }, Lines(ctrl).Skip(3).Take(2));
    }

    [Fact]
    public void A_settler_card_shows_job_and_home_only_when_known()
    {
        var (ctrl, _, world) = Make();
        world.Aim = WorkshopAim.TargetSettler;
        world.Settler = new WorkshopSettlerCard("a", "Ann") { JobText = "Farming", HomeName = "Sanctuary" };
        Assert.Equal(new[] { ("Ann", LineTone.Heading), ("Job: Farming", LineTone.Normal), ("Home: Sanctuary", LineTone.Normal) }, Lines(ctrl));

        world.Settler = new WorkshopSettlerCard("a", "Ann");

        Assert.Equal(new[] { ("Ann", LineTone.Heading) }, Lines(ctrl));
    }

    [Fact]
    public void The_store_scrap_and_repair_panels_ask_with_the_object_name_and_list_parts_and_costs()
    {
        var (ctrl, catalog, world) = Make();
        catalog.Stock["steel"] = 1;
        world.Aim = WorkshopAim.TargetObject;
        world.Target = Crate;

        ctrl.Handle(MenuInputAction.Favourite);
        Assert.Equal(new[] { ("Keep Crate in storage?", LineTone.Heading) }, Lines(ctrl));
        ctrl.Handle(MenuInputAction.Back);

        ctrl.Handle(MenuInputAction.Compare);
        Assert.Equal(
            new[] { ("Break down Crate for parts?", LineTone.Heading), ("You receive", LineTone.Heading), ("Wood x 2", LineTone.Normal) },
            Lines(ctrl));
        ctrl.Handle(MenuInputAction.Back);

        ctrl.Handle(MenuInputAction.Search);
        Assert.Equal(new[] { ("Restore Crate? Cost:", LineTone.Heading), ("Steel 1/4 short", LineTone.Bad) }, Lines(ctrl));
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Confirm"), new MenuHint(MenuInputAction.Back, "Cancel") }, HintsOf(ctrl));
    }

    [Fact]
    public void A_repair_that_cannot_be_afforded_explains_and_a_scrap_with_no_yield_lists_no_parts()
    {
        var (ctrl, catalog, world) = Make();
        catalog.Stock["steel"] = 1;
        world.Aim = WorkshopAim.TargetObject;
        world.Target = Crate with { RepairAffordable = false, Yield = [] };

        ctrl.Handle(MenuInputAction.Search);
        Assert.Equal(
            new[] { ("Restore Crate?", LineTone.Heading), ("You don't have the materials.", LineTone.Bad), ("Steel 1/4 short", LineTone.Bad) },
            Lines(ctrl));
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Back, "Close") }, HintsOf(ctrl));
        ctrl.Handle(MenuInputAction.Back);

        ctrl.Handle(MenuInputAction.Compare);
        Assert.Equal(new[] { ("Break down Crate for parts?", LineTone.Heading) }, Lines(ctrl));
    }

    [Fact]
    public void Exit_is_ignored_while_a_panel_is_open()
    {
        var (ctrl, _, world) = Make();
        world.Aim = WorkshopAim.TargetObject;
        world.Target = Crate;
        ctrl.Handle(MenuInputAction.Favourite);

        Assert.True(ctrl.Handle(MenuInputAction.Sort));

        Assert.True(ctrl.IsConfirming);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void An_object_action_that_is_not_possible_says_so_and_sends_nothing()
    {
        var cases = new (MenuInputAction Action, WorkshopTarget Target, string Message)[]
        {
            (MenuInputAction.Accept, new WorkshopTarget("o1", "Crate"), "This can't be moved."),
            (MenuInputAction.Favourite, new WorkshopTarget("o1", "Crate"), "This can't be stored."),
            (MenuInputAction.Compare, new WorkshopTarget("o1", "Crate"), "This can't be scrapped."),
            (MenuInputAction.Compare, new WorkshopTarget("o1", "Crate") { Scrappable = true, ScrapBlocked = true }, "This can't be scrapped right now."),
            (MenuInputAction.Search, new WorkshopTarget("o1", "Crate"), "This doesn't need repairs."),
            (MenuInputAction.Search, new WorkshopTarget("o1", "Crate") { Repairable = true, RepairBlocked = true }, "This can't be repaired right now."),
            (MenuInputAction.TabNext, new WorkshopTarget("o1", "Crate"), "This can't take a wire."),
        };

        foreach (var (action, target, message) in cases)
        {
            var (ctrl, _, world) = Make();
            world.Aim = WorkshopAim.TargetObject;
            world.Target = target;
            var intents = WorkshopTestHelpers.Capture(ctrl);

            Assert.True(ctrl.Handle(action));

            Assert.Equal(message, ctrl.Message);
            Assert.Empty(intents);
            Assert.False(ctrl.IsConfirming);
        }
    }

    [Fact]
    public void Moving_and_wiring_an_object_send_their_intents()
    {
        var (ctrl, _, world) = Make();
        world.Aim = WorkshopAim.TargetObject;
        world.Target = new WorkshopTarget("o1", "Crate") { Selectable = true, PowerConnectable = true };
        var intents = WorkshopTestHelpers.Capture(ctrl);

        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.TabNext);

        Assert.Equal(new WorkshopIntent[] { new MoveRequested("o1"), new WireRequested("o1") }, intents);
    }

    [Fact]
    public void Scrapping_a_held_object_that_cannot_be_scrapped_says_so()
    {
        var (ctrl, _, world) = Make();
        world.Aim = WorkshopAim.Holding;
        world.Target = null;

        Assert.True(ctrl.Handle(MenuInputAction.Compare));

        Assert.Equal("This can't be scrapped right now.", ctrl.Message);
        Assert.False(ctrl.IsConfirming);
    }

    [Fact]
    public void The_hints_for_a_held_wire_a_held_object_a_targeted_object_and_a_targeted_settler()
    {
        var (ctrl, _, world) = Make();
        world.Aim = WorkshopAim.WireHeld;
        var wire = HintsOf(ctrl);
        world.Placement = PlacementStatus.WireTooLong;
        var wireBlocked = HintsOf(ctrl);
        world.Placement = PlacementStatus.Valid;
        world.Aim = WorkshopAim.Holding;
        world.Target = new WorkshopTarget("o1", "Crate") { Scrappable = true };
        var holding = HintsOf(ctrl);
        world.Aim = WorkshopAim.TargetObject;
        world.Target = new WorkshopTarget("o1", "Crate")
        {
            Selectable = true, Storable = true, Scrappable = true, Repairable = true, PowerConnectable = true,
        };
        var target = HintsOf(ctrl);
        world.Aim = WorkshopAim.TargetSettler;
        world.Settler = new WorkshopSettlerCard("a", "Ann");
        var settler = HintsOf(ctrl);
        var exit = new MenuHint(MenuInputAction.Sort, "Exit");

        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Place wire end"), new MenuHint(MenuInputAction.Back, "Cancel"), exit }, wire);
        Assert.Equal("Place wire end (blocked)", wireBlocked[0].Label);
        Assert.Equal(
            new[] { new MenuHint(MenuInputAction.Accept, "Put down"), new MenuHint(MenuInputAction.Back, "Cancel"), new MenuHint(MenuInputAction.Compare, "Scrap"), exit },
            holding);
        Assert.Equal(
            new[]
            {
                new MenuHint(MenuInputAction.Accept, "Move"),
                new MenuHint(MenuInputAction.Back, "Exit"),
                new MenuHint(MenuInputAction.Compare, "Scrap"),
                new MenuHint(MenuInputAction.Favourite, "Store"),
                new MenuHint(MenuInputAction.Search, "Repair"),
                new MenuHint(MenuInputAction.TabNext, "Attach wire"),
                exit,
            },
            target);
        Assert.Equal(
            new[]
            {
                new MenuHint(MenuInputAction.Accept, "Command"),
                new MenuHint(MenuInputAction.Back, "Exit"),
                new MenuHint(MenuInputAction.Compare, "Move"),
                new MenuHint(MenuInputAction.Favourite, "Caravan"),
                exit,
            },
            settler);
    }

    [Fact]
    public void Browsing_hints_name_the_row_kind_and_back_goes_up_inside_a_category()
    {
        var (ctrl, _, _) = Make();
        var root = HintsOf(ctrl);
        ctrl.Handle(MenuInputAction.Right);
        var inside = HintsOf(ctrl);

        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Open"), new MenuHint(MenuInputAction.Back, "Exit") }, root.Take(2));
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Select"), new MenuHint(MenuInputAction.Back, "Up") }, inside.Take(2));
    }

    [Fact]
    public void The_placement_problem_is_shown_for_a_ghost_a_held_object_and_a_held_wire_only()
    {
        var (ctrl, _, world) = Make();
        world.Placement = PlacementStatus.NeedsWater;
        Assert.Equal(new[] { ("Furniture", LineTone.Heading), ("2 entries", LineTone.Muted) }, Lines(ctrl));

        world.Aim = WorkshopAim.Ghost;
        Assert.Equal(("This only fits in water.", LineTone.Bad), Lines(ctrl).Last());

        world.Aim = WorkshopAim.Holding;
        Assert.Equal(("This only fits in water.", LineTone.Bad), Lines(ctrl).Last());

        world.Aim = WorkshopAim.WireHeld;
        Assert.Equal(("This only fits in water.", LineTone.Bad), Lines(ctrl).Last());
    }

    [Fact]
    public void The_strip_flags_alerts_and_the_happiness_trend_and_reports_a_full_build_meter()
    {
        var (ctrl, _, world) = Make();
        world.Settlement = new WorkshopSettlement
        {
            Values = new Dictionary<SettlementStat, int> { [SettlementStat.Happiness] = 5 },
            Alerts = new HashSet<SettlementStat> { SettlementStat.Happiness, SettlementStat.Food },
            HappinessTrend = 1,
            BuildSize = 1.0,
        };

        Assert.Equal(
            new[]
            {
                ("Settlement", LineTone.Heading),
                ("People: --", LineTone.Normal),
                ("Food: -- alert", LineTone.Bad),
                ("Water: --", LineTone.Normal),
                ("Power: --", LineTone.Normal),
                ("Defense: --", LineTone.Normal),
                ("Beds: --", LineTone.Normal),
                ("Happiness: 5 alert rising", LineTone.Bad),
                ("Build size: 100% Full", LineTone.Bad),
            },
            Lines(ctrl).TakeLast(9));

        world.Settlement = world.Settlement with { HappinessTrend = -1, Alerts = new HashSet<SettlementStat>() };

        Assert.Equal(("Happiness: 5 falling", LineTone.Normal), Lines(ctrl).TakeLast(2).First());
    }

    [Theory]
    [InlineData(1.5, "Build size: 100% Full", LineTone.Bad)]
    [InlineData(0.9, "Build size: 90% Nearly full", LineTone.Bad)]
    [InlineData(0.894, "Build size: 89%", LineTone.Normal)]
    [InlineData(0.0, "Build size: 0%", LineTone.Normal)]
    [InlineData(-1.0, "Build size: 0%", LineTone.Normal)]
    public void The_build_meter_clamps_rounds_and_tones_by_how_full_it_is(double size, string text, LineTone tone)
    {
        var (ctrl, _, world) = Make();
        world.Settlement = new WorkshopSettlement { BuildSize = size };

        Assert.Equal((text, tone), Lines(ctrl).Last());
    }

    [Fact]
    public void A_ghost_that_is_short_of_materials_can_be_tracked_and_the_hint_flips_after_tracking()
    {
        var item = new WorkshopItem("l", "Lamp") { Materials = [new WorkshopMaterial("steel", "Steel", 3)] };
        var (ctrl, catalog, world) = Single(item);
        world.Aim = WorkshopAim.Ghost;
        catalog.Stock["steel"] = 1;
        var intents = WorkshopTestHelpers.Capture(ctrl);
        var before = HintsOf(ctrl)[2];

        ctrl.Handle(MenuInputAction.TabPrev);
        var tracking = HintsOf(ctrl)[2];
        var sentence = ctrl.Message;
        ctrl.Handle(MenuInputAction.TabPrev);

        Assert.Equal(new MenuHint(MenuInputAction.TabPrev, "Track materials"), before);
        Assert.Equal(new MenuHint(MenuInputAction.TabPrev, "Stop tracking"), tracking);
        Assert.Equal("Tracking materials.", sentence);
        Assert.Equal("Stopped tracking.", ctrl.Message);
        Assert.Equal(new WorkshopIntent[] { new TrackRequested("l", true), new TrackRequested("l", false) }, intents);
    }

    [Fact]
    public void Nodes_and_intents_compare_by_value()
    {
        var leaf = WorkshopNode.Leaf(new WorkshopItem("a", "Chair"));
        var twin = WorkshopNode.Leaf(new WorkshopItem("a", "Chair"));
        var category = WorkshopNode.Category("c", "Cat", leaf);

        Assert.NotSame(leaf, twin);
        Assert.Equal(leaf, twin);
        Assert.Equal(leaf.GetHashCode(), twin.GetHashCode());
        Assert.NotEqual(leaf, WorkshopNode.Leaf(new WorkshopItem("a", "Table")));
        Assert.False(leaf.IsCategory);
        Assert.Equal(("a", "Chair"), (leaf.Id, leaf.Name));
        Assert.True(category.IsCategory);
        Assert.Equal(new[] { leaf }, category.Children);
        Assert.Equal(new MoveRequested("a"), new MoveRequested("a"));
        Assert.NotEqual(new TrackRequested("a", true), new TrackRequested("a", false));
        Assert.Equal(new GhostCleared(), new GhostCleared());
    }

    [Fact]
    public void The_controller_needs_its_catalog_and_world()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkshopBuildController(null!, new FakeWorld()));
        Assert.Throws<ArgumentNullException>(() => new WorkshopBuildController(FakeCatalog.Sample(), null!));
    }
}
