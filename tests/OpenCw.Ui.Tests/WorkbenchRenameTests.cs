using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workbench;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WorkbenchRenameTests
{
    sealed class FakeRecipes : IWorkbenchRecipes
    {
        public string Title => "Bench";

        public string ApplyVerb => string.Empty;

        public string Stamp { get; set; } = "1";

        public List<WorkbenchTarget> TargetList { get; } = new() { new WorkbenchTarget("t1", "Rifle"), new WorkbenchTarget("t2", "Pistol") };

        public IReadOnlyList<WorkbenchTarget> Targets => TargetList;

        public IReadOnlyList<WorkbenchSlot> SlotsOf(string targetId) => new[] { new WorkbenchSlot("s1", "Barrel") };

        public IReadOnlyList<WorkbenchOption> OptionsOf(string targetId, string slotId) => Array.Empty<WorkbenchOption>();

        public WorkbenchOptionState StateOf(string targetId, string slotId, string optionId) => WorkbenchOptionState.Available;

        public IReadOnlyList<WorkshopMaterial> Requirements(string targetId, string slotId, string optionId) => Array.Empty<WorkshopMaterial>();

        public int AvailableCount(string materialId) => 0;

        public ActionResult Craft(string targetId, string slotId, string optionId) => new(true);
    }

    sealed class FakeRename : IWorkbenchRename
    {
        private readonly FakeRecipes _recipes;

        public FakeRename(FakeRecipes recipes)
        {
            _recipes = recipes;
        }

        public int MaxNameLength => 12;

        public ActionResult Result { get; set; } = new(true);

        public bool Throw { get; set; }

        public bool ThrowCurrent { get; set; }

        public List<(string Id, string Name)> Renames { get; } = new();

        public bool CanRename(string targetId) => Throw ? throw new InvalidOperationException("boom") : targetId != "t2";

        public string CurrentName(string targetId) => ThrowCurrent ? throw new InvalidOperationException("boom") : _recipes.TargetList.First(t => t.Id == targetId).Name;

        public string? Validate(string name) => Throw ? throw new InvalidOperationException("boom") : name.Contains('!') ? "No bangs." : null;

        public ActionResult Rename(string targetId, string name)
        {
            if (Throw) throw new InvalidOperationException("boom");
            Renames.Add((targetId, name));
            if (!Result.Ok) return Result;
            var index = _recipes.TargetList.FindIndex(t => t.Id == targetId);
            _recipes.TargetList[index] = _recipes.TargetList[index] with { Name = name };
            _recipes.Stamp += "x";
            return Result;
        }
    }

    static (WorkbenchController Ctrl, FakeRecipes Recipes, FakeRename Rename, List<RenameRequest> Requests) Make()
    {
        var recipes = new FakeRecipes();
        var rename = new FakeRename(recipes);
        var ctrl = new WorkbenchController(recipes, rename);
        var requests = new List<RenameRequest>();
        ctrl.RenameRequested += requests.Add;
        return (ctrl, recipes, rename, requests);
    }

    [Fact]
    public void Favourite_on_an_item_asks_the_host_for_a_prompt_seeded_with_the_current_name()
    {
        var (ctrl, _, _, requests) = Make();

        Assert.True(ctrl.Handle(MenuInputAction.Favourite));

        var request = Assert.Single(requests);
        Assert.Equal(WorkbenchController.RenameTitle, request.Title);
        Assert.Equal(WorkbenchController.RenamePrompt, request.Prompt);
        Assert.Equal("Rifle", request.Model.Value);
        Assert.Equal(12, request.Model.MaxLength);
    }

    [Fact]
    public void The_prompt_model_applies_the_rename_validator()
    {
        var (ctrl, _, _, requests) = Make();
        ctrl.Handle(MenuInputAction.Favourite);
        var model = requests.Single().Model;

        Assert.True(model.Validity.IsValid);
        model.Insert("!");

        Assert.False(model.Validity.IsValid);
        Assert.Equal("No bangs.", model.Validity.Message);
    }

    [Fact]
    public void The_rename_hint_shows_only_while_the_selected_item_can_be_renamed()
    {
        var (ctrl, _, _, _) = Make();

        Assert.Equal("Rename", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Favourite).Label);

        ctrl.Handle(MenuInputAction.Down);

        Assert.False(ctrl.CanRenameSelected);
        Assert.DoesNotContain(ctrl.Hints, hint => hint.Action == MenuInputAction.Favourite);
    }

    [Fact]
    public void An_item_that_cannot_be_renamed_raises_no_request()
    {
        var (ctrl, _, _, requests) = Make();
        ctrl.Handle(MenuInputAction.Down);

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Empty(requests);
    }

    [Fact]
    public void A_confirmed_name_is_trimmed_applied_and_shown_in_the_list()
    {
        var (ctrl, _, rename, requests) = Make();
        ctrl.Handle(MenuInputAction.Favourite);

        requests.Single().Complete("  Blaster ");

        Assert.Equal(("t1", "Blaster"), Assert.Single(rename.Renames));
        Assert.Equal(new[] { "Blaster", "Pistol" }, WorkshopTestHelpers.RowTexts(ctrl.List));
        Assert.Equal(WorkbenchController.RenamedText, ctrl.Message);
    }

    [Fact]
    public void Cancelling_an_empty_name_or_the_same_name_changes_nothing()
    {
        var (ctrl, _, rename, requests) = Make();
        ctrl.Handle(MenuInputAction.Favourite);
        requests.Single().Complete(null);

        ctrl.Handle(MenuInputAction.Favourite);
        requests.Last().Complete("   ");

        ctrl.Handle(MenuInputAction.Favourite);
        requests.Last().Complete(" Rifle ");

        Assert.Empty(rename.Renames);
        Assert.Equal(string.Empty, ctrl.Message);
    }

    [Fact]
    public void A_prompt_result_counts_once()
    {
        var (ctrl, _, rename, requests) = Make();
        ctrl.Handle(MenuInputAction.Favourite);
        var request = requests.Single();

        request.Complete("First");
        request.Complete("Second");

        Assert.Equal(("t1", "First"), Assert.Single(rename.Renames));
    }

    [Fact]
    public void A_failed_rename_shows_the_reason_and_keeps_the_old_name()
    {
        var (ctrl, _, rename, requests) = Make();
        rename.Result = new ActionResult(false, "Name taken.");
        ctrl.Handle(MenuInputAction.Favourite);

        requests.Single().Complete("Blaster");

        Assert.Equal("Name taken.", ctrl.Message);
        Assert.Equal(new[] { "Rifle", "Pistol" }, WorkshopTestHelpers.RowTexts(ctrl.List));
    }

    [Fact]
    public void A_failed_rename_without_a_reason_uses_the_standard_failure_text()
    {
        var (ctrl, _, rename, requests) = Make();
        rename.Result = new ActionResult(false);
        ctrl.Handle(MenuInputAction.Favourite);

        requests.Single().Complete("Blaster");

        Assert.Equal(WorkbenchController.FailedText, ctrl.Message);
    }

    [Fact]
    public void An_item_that_vanished_before_the_prompt_closed_says_it_changed()
    {
        var (ctrl, recipes, rename, requests) = Make();
        ctrl.Handle(MenuInputAction.Favourite);
        recipes.TargetList.RemoveAt(0);

        requests.Single().Complete("Blaster");

        Assert.Empty(rename.Renames);
        Assert.Equal(WorkbenchController.ChangedText, ctrl.Message);
    }

    [Fact]
    public void Without_a_rename_service_there_is_no_hint_and_no_request()
    {
        var ctrl = new WorkbenchController(new FakeRecipes());
        var requests = new List<RenameRequest>();
        ctrl.RenameRequested += requests.Add;

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.False(ctrl.CanRenameSelected);
        Assert.DoesNotContain(ctrl.Hints, hint => hint.Action == MenuInputAction.Favourite);
        Assert.Empty(requests);
    }

    [Fact]
    public void Rename_is_not_offered_once_inside_an_item()
    {
        var (ctrl, _, _, requests) = Make();
        ctrl.Handle(MenuInputAction.Accept);

        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Equal(WorkbenchView.Slots, ctrl.View);
        Assert.False(ctrl.CanRenameSelected);
        Assert.Empty(requests);
    }

    [Fact]
    public void With_no_host_listening_the_request_is_refused_with_a_blocked_sound()
    {
        var recipes = new FakeRecipes();
        var ctrl = new WorkbenchController(recipes, new FakeRename(recipes));
        var sounds = new List<string>();
        ctrl.SoundRequested += sounds.Add;

        Assert.True(ctrl.Handle(MenuInputAction.Favourite));

        Assert.Equal(new[] { "menu.blocked" }, sounds);
    }

    [Fact]
    public void A_rename_service_that_throws_on_the_can_rename_check_hides_the_hint()
    {
        var (ctrl, _, rename, requests) = Make();
        rename.Throw = true;

        Assert.False(ctrl.CanRenameSelected);
        Assert.DoesNotContain(ctrl.Hints, hint => hint.Action == MenuInputAction.Favourite);
        ctrl.Handle(MenuInputAction.Favourite);

        Assert.Empty(requests);
    }

    [Fact]
    public void A_rename_service_that_throws_when_reading_the_name_reports_a_failure_and_raises_no_request()
    {
        var (ctrl, _, rename, requests) = Make();
        rename.ThrowCurrent = true;

        Assert.True(ctrl.Handle(MenuInputAction.Favourite));

        Assert.Empty(requests);
        Assert.Equal(WorkbenchController.FailedText, ctrl.Message);
    }

    [Fact]
    public void A_rename_service_that_throws_on_rename_reports_a_failure_and_keeps_the_old_name()
    {
        var (ctrl, _, rename, requests) = Make();
        ctrl.Handle(MenuInputAction.Favourite);
        rename.Throw = true;

        requests.Single().Complete("Blaster");

        Assert.Equal(WorkbenchController.FailedText, ctrl.Message);
        Assert.Equal(new[] { "Rifle", "Pistol" }, WorkshopTestHelpers.RowTexts(ctrl.List));
    }

    [Fact]
    public void A_validator_that_throws_makes_the_prompt_invalid_instead_of_crashing()
    {
        var (ctrl, _, rename, requests) = Make();
        ctrl.Handle(MenuInputAction.Favourite);
        var model = requests.Single().Model;
        rename.Throw = true;

        Assert.False(model.Validity.IsValid);
        Assert.Equal(WorkbenchController.FailedText, model.Validity.Message);
    }
}
