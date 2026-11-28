using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workbench;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WorkbenchControllerEdgeTests
{
    sealed class StubRecipes : IWorkbenchRecipes
    {
        public string Title => "Bench";

        public string ApplyVerb { get; set; } = string.Empty;

        public string Stamp { get; set; } = "1";

        public int Have { get; set; } = 2;

        public ActionResult Result { get; set; } = new(true);

        public bool CraftThrows { get; set; }

        public Action? OnCraft { get; set; }

        public List<WorkbenchTarget> TargetList { get; } = new() { new WorkbenchTarget("t1", "Rifle") };

        public List<WorkbenchSlot> SlotList { get; } = new() { new WorkbenchSlot("s1", "Barrel") };

        public List<WorkbenchOption> OptionList { get; } = new() { new WorkbenchOption("o1", "Long"), new WorkbenchOption("o2", "Short") };

        public Dictionary<string, WorkbenchOptionState> States { get; } = new();

        public List<WorkshopMaterial> Needs { get; } = new() { new WorkshopMaterial("steel", "Steel", 4) };

        public IReadOnlyList<WorkbenchTarget> Targets => TargetList;

        public IReadOnlyList<WorkbenchSlot> SlotsOf(string targetId) => SlotList.ToArray();

        public IReadOnlyList<WorkbenchOption> OptionsOf(string targetId, string slotId) => OptionList.ToArray();

        public WorkbenchOptionState StateOf(string targetId, string slotId, string optionId) =>
            States.GetValueOrDefault(optionId, WorkbenchOptionState.Available);

        public IReadOnlyList<WorkshopMaterial> Requirements(string targetId, string slotId, string optionId) => Needs.ToArray();

        public int AvailableCount(string materialId) => Have;

        public ActionResult Craft(string targetId, string slotId, string optionId)
        {
            if (CraftThrows) throw new InvalidOperationException("boom");
            OnCraft?.Invoke();
            return Result;
        }
    }

    static (WorkbenchController Ctrl, StubRecipes Recipes) Make()
    {
        var recipes = new StubRecipes();
        return (new WorkbenchController(recipes), recipes);
    }

    static void Press(WorkbenchController ctrl, params MenuInputAction[] actions)
    {
        foreach (var action in actions) ctrl.Handle(action);
    }

    static (string, LineTone)[] Lines(WorkbenchController ctrl) => ctrl.DetailLines.Select(line => (line.Text, line.Tone)).ToArray();

    [Fact]
    public void A_craft_that_replaces_the_target_returns_to_the_target_list_instead_of_an_empty_options_list()
    {
        var (ctrl, recipes) = Make();
        recipes.OnCraft = () =>
        {
            recipes.TargetList.Clear();
            recipes.TargetList.Add(new WorkbenchTarget("t2", "Rifle (long)"));
            recipes.Stamp = "2";
        };
        Press(ctrl, MenuInputAction.Accept, MenuInputAction.Accept, MenuInputAction.Accept, MenuInputAction.Accept);

        Assert.Equal(WorkbenchView.Targets, ctrl.View);
        Assert.Equal("Rifle (long)", ctrl.List.RowText(0));
        Assert.Equal(WorkbenchController.DoneText, ctrl.Message);
    }

    [Theory]
    [InlineData(WorkbenchOptionState.Available, "")]
    [InlineData(WorkbenchOptionState.Installed, "Installed")]
    [InlineData(WorkbenchOptionState.LackingMaterials, "Lacking materials")]
    [InlineData(WorkbenchOptionState.PerkRequired, "Perk required")]
    [InlineData(WorkbenchOptionState.Unavailable, "Unavailable")]
    public void Every_option_state_has_its_word_and_available_has_none(WorkbenchOptionState state, string expected)
    {
        Assert.Equal(expected, WorkbenchController.StateWord(state));
    }

    [Fact]
    public void The_header_grows_with_each_level()
    {
        var (ctrl, _) = Make();
        var headers = new List<string> { ctrl.Header };

        foreach (var _ in new[] { 1, 2, 3 })
        {
            ctrl.Handle(MenuInputAction.Accept);
            headers.Add(ctrl.Header);
        }

        Assert.Equal(new[] { string.Empty, "Rifle", "Rifle > Barrel", "Rifle > Barrel > Long" }, headers);
    }

    [Fact]
    public void The_hints_change_with_the_level()
    {
        var (ctrl, _) = Make();
        var targets = ctrl.Hints.ToArray();
        ctrl.Handle(MenuInputAction.Accept);
        var slots = ctrl.Hints.ToArray();
        ctrl.Handle(MenuInputAction.Accept);
        var options = ctrl.Hints.ToArray();
        ctrl.Handle(MenuInputAction.Accept);
        var confirm = ctrl.Hints.ToArray();

        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Select"), new MenuHint(MenuInputAction.Back, "Close") }, targets);
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Select"), new MenuHint(MenuInputAction.Back, "Back") }, slots);
        Assert.Equal(slots, options);
        Assert.Equal(new[] { new MenuHint(MenuInputAction.Accept, "Apply"), new MenuHint(MenuInputAction.Back, "Cancel") }, confirm);
    }

    [Fact]
    public void A_blank_apply_verb_falls_back_to_apply()
    {
        var (ctrl, recipes) = Make();
        recipes.ApplyVerb = "   ";

        Assert.Equal(WorkbenchController.DefaultVerb, ctrl.Verb);
    }

    [Fact]
    public void A_rejected_craft_with_no_reason_says_it_failed()
    {
        var (ctrl, recipes) = Make();
        recipes.Result = new ActionResult(false);
        var rejected = new List<string>();
        ctrl.CraftRejected += rejected.Add;
        Press(ctrl, MenuInputAction.Accept, MenuInputAction.Accept, MenuInputAction.Accept, MenuInputAction.Accept);

        Assert.Equal(new[] { WorkbenchController.FailedText }, rejected);
        Assert.Equal(WorkbenchController.FailedText, ctrl.Message);
        Assert.Equal(WorkbenchView.Options, ctrl.View);
    }

    [Fact]
    public void A_craft_that_throws_does_not_leave_the_bench_locked()
    {
        var (ctrl, recipes) = Make();
        Press(ctrl, MenuInputAction.Accept, MenuInputAction.Accept, MenuInputAction.Accept);
        recipes.CraftThrows = true;

        Assert.Throws<InvalidOperationException>(() => ctrl.Handle(MenuInputAction.Accept));
        recipes.CraftThrows = false;
        ctrl.Handle(MenuInputAction.Back);

        Assert.Equal(WorkbenchView.Options, ctrl.View);
    }

    [Fact]
    public void Moving_in_the_confirmation_is_not_handled()
    {
        var (ctrl, _) = Make();
        Press(ctrl, MenuInputAction.Accept, MenuInputAction.Accept, MenuInputAction.Accept);

        Assert.False(ctrl.Handle(MenuInputAction.Down));
    }

    [Fact]
    public void Going_back_from_the_confirmation_keeps_the_option_selected_and_a_finished_bench_ignores_input()
    {
        var (ctrl, _) = Make();
        Press(ctrl, MenuInputAction.Accept, MenuInputAction.Accept, MenuInputAction.Down, MenuInputAction.Accept, MenuInputAction.Back);

        Assert.Equal(WorkbenchView.Options, ctrl.View);
        Assert.Equal("o2", ctrl.Rows.Selected!.Key);

        Press(ctrl, MenuInputAction.Back, MenuInputAction.Back, MenuInputAction.Back);

        Assert.True(ctrl.IsFinished);
        Assert.False(ctrl.Handle(MenuInputAction.Accept));
    }

    [Fact]
    public void A_slot_that_disappears_sends_the_bench_back_to_the_slots()
    {
        var (ctrl, recipes) = Make();
        Press(ctrl, MenuInputAction.Accept, MenuInputAction.Accept);
        recipes.SlotList.Clear();
        recipes.Stamp = "2";

        ctrl.Tick(0.1);

        Assert.Equal(WorkbenchView.Slots, ctrl.View);
        Assert.Equal(new[] { (WorkbenchController.EmptyText, LineTone.Muted) }, Lines(ctrl));
    }

    [Fact]
    public void An_option_that_disappears_while_confirming_sends_the_bench_back_to_the_options()
    {
        var (ctrl, recipes) = Make();
        Press(ctrl, MenuInputAction.Accept, MenuInputAction.Accept, MenuInputAction.Accept);
        recipes.OptionList.RemoveAt(0);
        recipes.Stamp = "2";

        ctrl.Tick(0.1);

        Assert.Equal(WorkbenchView.Options, ctrl.View);
        Assert.Equal(WorkbenchController.ChangedText, ctrl.Message);
        Assert.Equal(new[] { "Short" }, WorkshopTestHelpers.RowTexts(ctrl.List));
    }

    [Fact]
    public void An_option_with_no_requirements_has_no_needs_heading()
    {
        var (ctrl, recipes) = Make();
        recipes.Needs.Clear();
        Press(ctrl, MenuInputAction.Accept, MenuInputAction.Accept);

        Assert.Equal(new[] { ("Long", LineTone.Heading) }, Lines(ctrl));
    }

    [Fact]
    public void An_installed_option_shows_its_state_in_the_detail_before_the_needs()
    {
        var (ctrl, recipes) = Make();
        recipes.States["o1"] = WorkbenchOptionState.Installed;
        Press(ctrl, MenuInputAction.Accept, MenuInputAction.Accept);

        Assert.Equal(
            new[] { ("Long", LineTone.Heading), ("Installed", LineTone.Bad), ("Needs", LineTone.Heading), ("Steel 2/4 short", LineTone.Bad) },
            Lines(ctrl));
    }

    [Fact]
    public void Having_exactly_enough_of_a_material_is_not_short_and_reads_normal()
    {
        var (ctrl, recipes) = Make();
        recipes.Have = 4;
        Press(ctrl, MenuInputAction.Accept, MenuInputAction.Accept, MenuInputAction.Accept);

        Assert.Equal(
            new[] { ("Apply Long?", LineTone.Heading), ("Needs", LineTone.Heading), ("Steel 4/4", LineTone.Normal) },
            Lines(ctrl));
    }

    [Fact]
    public void A_slot_with_nothing_installed_shows_no_detail()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Empty(ctrl.DetailLines);
    }

    [Fact]
    public void A_successful_craft_keeps_the_crafted_option_selected()
    {
        var (ctrl, _) = Make();
        Press(ctrl, MenuInputAction.Accept, MenuInputAction.Accept, MenuInputAction.Down, MenuInputAction.Accept, MenuInputAction.Accept);

        Assert.Equal(WorkbenchController.DoneText, ctrl.Message);
        Assert.Equal("o2", ctrl.Rows.Selected!.Key);
    }

    [Fact]
    public void The_bench_needs_its_recipes()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkbenchController(null!));
    }
}
