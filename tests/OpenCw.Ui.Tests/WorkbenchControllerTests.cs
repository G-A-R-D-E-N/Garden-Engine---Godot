using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workbench;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WorkbenchControllerTests
{
    sealed class FakeRecipes : IWorkbenchRecipes
    {
        public string Title { get; set; } = "Weapon bench";

        public string ApplyVerb { get; set; } = string.Empty;

        public string Stamp { get; set; } = "1";

        public List<WorkbenchTarget> TargetList { get; } = new() { new WorkbenchTarget("t1", "Rifle") { Description = "A long rifle." } };

        public List<WorkbenchOption> OptionList { get; } = new()
        {
            new WorkbenchOption("o1", "Long") { Description = "Longer barrel." },
            new WorkbenchOption("o2", "Short"),
            new WorkbenchOption("o3", "Heavy"),
        };

        public Dictionary<string, WorkbenchOptionState> States { get; } = new()
        {
            ["o2"] = WorkbenchOptionState.Installed,
            ["o3"] = WorkbenchOptionState.LackingMaterials,
        };

        public ActionResult Result { get; set; } = new(true);

        public Action? OnCraft { get; set; }

        public List<string> Crafts { get; } = new();

        public IReadOnlyList<WorkbenchTarget> Targets => TargetList;

        public IReadOnlyList<WorkbenchSlot> SlotsOf(string targetId) =>
            targetId == "t1" ? new[] { new WorkbenchSlot("s1", "Barrel") { Current = "Standard" } } : Array.Empty<WorkbenchSlot>();

        public IReadOnlyList<WorkbenchOption> OptionsOf(string targetId, string slotId) =>
            targetId == "t1" && slotId == "s1" ? OptionList.ToArray() : Array.Empty<WorkbenchOption>();

        public WorkbenchOptionState StateOf(string targetId, string slotId, string optionId) =>
            States.GetValueOrDefault(optionId, WorkbenchOptionState.Available);

        public IReadOnlyList<WorkshopMaterial> Requirements(string targetId, string slotId, string optionId) =>
            new[] { new WorkshopMaterial("steel", "Steel", 4) };

        public int AvailableCount(string materialId) => 2;

        public ActionResult Craft(string targetId, string slotId, string optionId)
        {
            Crafts.Add(optionId);
            OnCraft?.Invoke();
            return Result;
        }
    }

    static (WorkbenchController Ctrl, FakeRecipes Recipes) Make()
    {
        var recipes = new FakeRecipes();
        return (new WorkbenchController(recipes), recipes);
    }

    static void ToOptions(WorkbenchController ctrl) =>
        Assert.True(ctrl.Handle(MenuInputAction.Accept) && ctrl.Handle(MenuInputAction.Accept));

    [Fact]
    public void The_bench_walks_targets_then_slots_then_options_with_state_words()
    {
        var (ctrl, _) = Make();
        Assert.Equal(new[] { "Rifle" }, WorkshopTestHelpers.RowTexts(ctrl.List));
        Assert.Equal("A long rifle.", WorkshopTestHelpers.Texts(ctrl).Single());

        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(WorkbenchView.Slots, ctrl.View);
        Assert.Equal("Now: Standard", WorkshopTestHelpers.Texts(ctrl).Single());

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(WorkbenchView.Options, ctrl.View);
        Assert.Equal(new[] { "Long", "Short - Installed", "Heavy - Lacking materials" }, WorkshopTestHelpers.RowTexts(ctrl.List));
        Assert.Equal("Rifle > Barrel", ctrl.Header);
    }

    [Fact]
    public void An_option_that_is_not_available_says_its_state_and_does_not_open_the_confirmation()
    {
        var (ctrl, _) = Make();
        ToOptions(ctrl);
        ctrl.Handle(MenuInputAction.Down);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("Installed.", ctrl.Message);
        Assert.Equal(WorkbenchView.Options, ctrl.View);
    }

    [Fact]
    public void Options_detail_shows_requirements_with_have_and_need()
    {
        var (ctrl, _) = Make();
        ToOptions(ctrl);

        var texts = WorkshopTestHelpers.Texts(ctrl);

        Assert.Contains("Longer barrel.", texts);
        Assert.Contains("Steel 2/4 short", texts);
    }

    [Fact]
    public void The_confirm_verb_defaults_to_apply_and_follows_the_bench()
    {
        var (ctrl, recipes) = Make();
        ToOptions(ctrl);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(WorkbenchView.Confirm, ctrl.View);
        Assert.Equal("Apply", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Accept).Label);

        recipes.ApplyVerb = "Craft";
        Assert.Equal("Craft", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Accept).Label);
        Assert.Equal("Craft Long?", WorkshopTestHelpers.Texts(ctrl)[0]);
    }

    [Fact]
    public void Confirming_crafts_once_reports_completion_and_returns_to_the_options()
    {
        var (ctrl, recipes) = Make();
        var done = new List<string>();
        ctrl.CraftCompleted += done.Add;
        ToOptions(ctrl);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "o1" }, recipes.Crafts);
        Assert.Equal(new[] { "o1" }, done);
        Assert.Equal(WorkbenchView.Options, ctrl.View);
        Assert.Equal(WorkbenchController.DoneText, ctrl.Message);
    }

    [Fact]
    public void A_rejected_craft_reports_the_reason_and_not_completion()
    {
        var (ctrl, recipes) = Make();
        recipes.Result = new ActionResult(false, "Bench is busy.");
        var done = 0;
        var rejected = new List<string>();
        ctrl.CraftCompleted += _ => done++;
        ctrl.CraftRejected += rejected.Add;
        ToOptions(ctrl);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(0, done);
        Assert.Equal(new[] { "Bench is busy." }, rejected);
        Assert.Equal("Bench is busy.", ctrl.Message);
        Assert.Equal(WorkbenchView.Options, ctrl.View);
    }

    [Fact]
    public void Choosing_an_option_the_player_lacks_the_materials_for_raises_the_build_fail_cue()
    {
        var (ctrl, _) = Make();
        ToOptions(ctrl);
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Down);
        var sounds = new List<string>();
        ctrl.SoundRequested += sounds.Add;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "bench.build_fail" }, sounds);
    }

    [Fact]
    public void Choosing_an_installed_option_keeps_the_shared_blocked_cue()
    {
        var (ctrl, _) = Make();
        ToOptions(ctrl);
        ctrl.Handle(MenuInputAction.Down);
        var sounds = new List<string>();
        ctrl.SoundRequested += sounds.Add;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "menu.blocked" }, sounds);
    }

    [Fact]
    public void Input_during_a_craft_cannot_start_a_second_one()
    {
        var (ctrl, recipes) = Make();
        recipes.OnCraft = () =>
        {
            ctrl.Handle(MenuInputAction.Accept);
            ctrl.Handle(MenuInputAction.Accept);
        };
        ToOptions(ctrl);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Single(recipes.Crafts);
    }

    [Fact]
    public void A_stamp_change_while_confirming_returns_to_the_options_when_the_option_is_no_longer_available()
    {
        var (ctrl, recipes) = Make();
        ToOptions(ctrl);
        ctrl.Handle(MenuInputAction.Accept);

        recipes.States["o1"] = WorkbenchOptionState.LackingMaterials;
        recipes.Stamp = "2";
        ctrl.Tick(0.1);

        Assert.Equal(WorkbenchView.Options, ctrl.View);
        Assert.Equal(WorkbenchController.ChangedText, ctrl.Message);
        Assert.Equal("Long - Lacking materials", ctrl.List.RowText(0));
        Assert.Empty(recipes.Crafts);
    }

    [Fact]
    public void An_unchanged_stamp_does_not_rebuild_and_a_changed_one_refreshes_rows_keeping_the_selection()
    {
        var (ctrl, recipes) = Make();
        ToOptions(ctrl);
        ctrl.Handle(MenuInputAction.Down);
        recipes.States["o2"] = WorkbenchOptionState.Available;
        ctrl.Tick(0.1);
        Assert.Equal("Short - Installed", ctrl.List.RowText(1));

        recipes.Stamp = "2";
        ctrl.Tick(0.1);

        Assert.Equal("Short", ctrl.List.RowText(1));
        Assert.Equal("o2", ctrl.Rows.Selected!.Key);
    }

    [Fact]
    public void A_confirm_that_goes_stale_between_the_tick_and_the_press_is_rechecked_before_crafting()
    {
        var (ctrl, recipes) = Make();
        ToOptions(ctrl);
        ctrl.Handle(MenuInputAction.Accept);
        recipes.States["o1"] = WorkbenchOptionState.PerkRequired;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Empty(recipes.Crafts);
        Assert.Equal(WorkbenchView.Options, ctrl.View);
        Assert.Equal(WorkbenchController.ChangedText, ctrl.Message);
    }

    [Fact]
    public void When_the_target_disappears_the_menu_falls_back_to_the_targets()
    {
        var (ctrl, recipes) = Make();
        ToOptions(ctrl);

        recipes.TargetList.Clear();
        recipes.Stamp = "2";
        ctrl.Tick(0.1);

        Assert.Equal(WorkbenchView.Targets, ctrl.View);
        Assert.Equal(WorkbenchController.EmptyText, ctrl.DetailLines.Single().Text);
    }

    [Fact]
    public void Back_steps_up_view_by_view_and_finishes_from_the_targets()
    {
        var (ctrl, _) = Make();
        ToOptions(ctrl);
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Back);
        Assert.Equal(WorkbenchView.Options, ctrl.View);
        ctrl.Handle(MenuInputAction.Back);
        Assert.Equal(WorkbenchView.Slots, ctrl.View);
        ctrl.Handle(MenuInputAction.Back);
        Assert.Equal(WorkbenchView.Targets, ctrl.View);
        Assert.False(ctrl.IsFinished);
        ctrl.Handle(MenuInputAction.Back);

        Assert.True(ctrl.IsFinished);
    }

    [Fact]
    public void The_title_comes_from_the_bench()
    {
        var (ctrl, _) = Make();

        Assert.Equal("Weapon bench", ctrl.Title);
    }

    [Theory]
    [InlineData(WorkbenchOptionState.Available, "")]
    [InlineData(WorkbenchOptionState.Installed, "Installed")]
    [InlineData(WorkbenchOptionState.LackingMaterials, "Lacking materials")]
    [InlineData(WorkbenchOptionState.PerkRequired, "Perk required")]
    [InlineData(WorkbenchOptionState.Unavailable, "Unavailable")]
    public void StateWord_names_every_option_state(WorkbenchOptionState state, string expected)
    {
        Assert.Equal(expected, WorkbenchController.StateWord(state));
    }
}
