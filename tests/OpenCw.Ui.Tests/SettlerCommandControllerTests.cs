using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettlerCommandControllerTests
{
    static (SettlerCommandController Ctrl, FakeSettlements Provider) Make(string? settler = null)
    {
        var provider = new FakeSettlements();
        return (new SettlerCommandController(provider, settler), provider);
    }

    static void Press(SettlerCommandController ctrl, params MenuInputAction[] actions)
    {
        foreach (var action in actions) ctrl.Handle(action);
    }

    static string[] Rows(SettlerCommandController ctrl) => WorkshopTestHelpers.RowTexts(ctrl.List);

    static (SettlerCommandController Ctrl, FakeSettlements Provider) AtTargets()
    {
        var (ctrl, provider) = Make("a");
        ctrl.Handle(MenuInputAction.Accept);
        Assert.Equal(CommandLevel.Targets, ctrl.Level);
        return (ctrl, provider);
    }

    [Fact]
    public void The_first_level_lists_settlers_with_their_home_and_a_card_for_the_selected_one()
    {
        var (ctrl, _) = Make();

        Assert.Equal(CommandLevel.Settlers, ctrl.Level);
        Assert.Equal(new[] { "Ann - Sanctuary", "Bob - Sanctuary" }, Rows(ctrl));
        Assert.Equal(new[] { "Ann", "Farming - Crop" }, WorkshopTestHelpers.Texts(ctrl));
        Assert.Equal("Settler orders", ctrl.Title);
    }

    [Fact]
    public void Choosing_a_settler_opens_the_command_list_named_for_them()
    {
        var (ctrl, _) = Make();
        ctrl.Handle(MenuInputAction.Down);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(CommandLevel.Commands, ctrl.Level);
        Assert.Equal("b", ctrl.SettlerId);
        Assert.Equal("Bob", ctrl.Header);
        Assert.Equal(new[] { "Command", "Move", "Caravan", "Clear work", "Clear bed" }, Rows(ctrl));
    }

    [Fact]
    public void Starting_on_a_known_settler_skips_the_settler_list_and_back_closes()
    {
        var (ctrl, _) = Make("a");

        Assert.Equal(CommandLevel.Commands, ctrl.Level);
        Assert.Equal("Close", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Back).Label);

        ctrl.Handle(MenuInputAction.Back);

        Assert.True(ctrl.IsFinished);
        Assert.False(ctrl.IsConfirmed);
    }

    [Fact]
    public void An_unknown_starting_settler_falls_back_to_the_settler_list()
    {
        var (ctrl, _) = Make("zzz");

        Assert.Equal(CommandLevel.Settlers, ctrl.Level);
    }

    [Fact]
    public void Back_steps_out_one_level_at_a_time_and_returns_to_the_same_settler()
    {
        var (ctrl, _) = Make();
        Press(ctrl, MenuInputAction.Down, MenuInputAction.Accept);
        Assert.Equal("Back", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Back).Label);

        ctrl.Handle(MenuInputAction.Back);

        Assert.Equal(CommandLevel.Settlers, ctrl.Level);
        Assert.Equal("Bob", ctrl.Rows.Selected!.Text.Split(' ')[0]);
        Assert.False(ctrl.IsFinished);

        ctrl.Handle(MenuInputAction.Back);

        Assert.True(ctrl.IsFinished);
    }

    [Fact]
    public void A_command_the_settler_cannot_take_is_disabled_with_the_providers_reason()
    {
        var (ctrl, provider) = Make("a");
        provider.SettlerList[0] = provider.SettlerList[0] with { CanMove = false, MoveReason = "Ann is in combat." };
        provider.Raise();
        ctrl.Handle(MenuInputAction.Down);

        Assert.Contains(ctrl.DetailLines, line => line.Text == "Ann is in combat." && line.Tone == LineTone.Bad);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("Ann is in combat.", ctrl.Message);
        Assert.Empty(provider.Calls);
        Assert.Equal(CommandLevel.Commands, ctrl.Level);
    }

    [Fact]
    public void A_disabled_command_without_a_reason_uses_a_default_sentence()
    {
        var (ctrl, provider) = Make("a");
        provider.SettlerList[0] = provider.SettlerList[0] with { CanCaravan = false };
        provider.Raise();
        Press(ctrl, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Accept);

        Assert.Equal("Ann can't be sent on a caravan route.", ctrl.Message);
    }

    [Fact]
    public void The_command_choice_lists_work_objects_and_marks_one_in_use()
    {
        var (ctrl, _) = AtTargets();

        Assert.Equal(new[] { "Field", "Tower - in use" }, Rows(ctrl));
        Assert.Equal("Choose a task for Ann.", ctrl.Header);
        Assert.Equal("Assign", ctrl.Hints.Single(hint => hint.Action == MenuInputAction.Accept).Label);
        Assert.Contains(ctrl.DetailLines, line => line.Text == "Needs a worker: yes");
    }

    [Fact]
    public void A_work_object_in_use_can_be_taken_over_and_the_card_says_from_whom()
    {
        var (ctrl, provider) = AtTargets();
        ctrl.Handle(MenuInputAction.Down);

        Assert.Contains(ctrl.DetailLines, line => line.Text == "In use by Cy" && line.Tone == LineTone.Bad);
        Assert.Contains(ctrl.DetailLines, line => line.Text == "Assigning it takes it from Cy.");
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "job:a>t2" }, provider.Calls);
        Assert.True(ctrl.IsFinished);
    }

    [Fact]
    public void Clearing_work_asks_the_provider_reports_the_change_and_finishes()
    {
        var (ctrl, provider) = Make("a");
        var changes = new List<(string, SettlerChange)>();
        ctrl.SettlerChanged += (id, change) => changes.Add((id, change));
        Press(ctrl, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down);
        Assert.Equal(SettlerCommandController.UnassignKey, ctrl.Rows.Selected!.Key);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "unassign:a" }, provider.Calls);
        Assert.Equal(new[] { ("a", SettlerChange.Unassigned) }, changes);
        Assert.Equal("Ann is no longer on the job.", ctrl.Message);
        Assert.True(ctrl.IsConfirmed);
        Assert.Equal(string.Empty, provider.SettlerList[0].WorkObject);
    }

    [Fact]
    public void Clearing_a_bed_leaves_the_work_object_alone()
    {
        var (ctrl, provider) = Make("a");
        provider.SettlerList[0] = provider.SettlerList[0] with { BedObject = "Bunk" };
        provider.Raise();
        Press(ctrl, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down);
        Assert.Equal(SettlerCommandController.UnassignBedKey, ctrl.Rows.Selected!.Key);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "unassignbed:a" }, provider.Calls);
        Assert.Equal("Ann no longer has a bed.", ctrl.Message);
        Assert.Equal(string.Empty, provider.SettlerList[0].BedObject);
        Assert.Equal("Crop", provider.SettlerList[0].WorkObject);
    }

    [Fact]
    public void Clearing_what_the_settler_does_not_have_is_disabled_with_a_reason()
    {
        var (ctrl, provider) = Make("a");
        Press(ctrl, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down);

        Assert.Contains(ctrl.DetailLines, line => line.Text == "Ann has no bed assigned." && line.Tone == LineTone.Bad);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("Ann has no bed assigned.", ctrl.Message);
        Assert.Empty(provider.Calls);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void A_failed_clear_shows_the_reason_and_stays_open()
    {
        var (ctrl, provider) = Make("a");
        provider.Next = new ActionResult(false, "Nothing to unassign.");
        Press(ctrl, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Accept);

        Assert.Equal("Nothing to unassign.", ctrl.Message);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void Assigning_a_work_object_calls_the_provider_reports_the_change_and_finishes()
    {
        var (ctrl, provider) = AtTargets();
        var changes = new List<(string, SettlerChange)>();
        var sounds = new List<string>();
        var results = new List<bool>();
        ctrl.SettlerChanged += (id, change) => changes.Add((id, change));
        ctrl.SoundRequested += sounds.Add;
        ctrl.Finished += results.Add;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "job:a>t1" }, provider.Calls);
        Assert.Equal(new[] { ("a", SettlerChange.Job) }, changes);
        Assert.Contains("settler.assign", sounds);
        Assert.Equal("Ann is on the job.", ctrl.Message);
        Assert.Equal(new[] { true }, results);
        Assert.True(ctrl.IsConfirmed);
    }

    [Fact]
    public void A_failed_assignment_shows_the_reason_and_stays_open()
    {
        var (ctrl, provider) = AtTargets();
        var changes = 0;
        ctrl.SettlerChanged += (_, _) => changes++;
        provider.Next = new ActionResult(false, "Not now.");

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal("Not now.", ctrl.Message);
        Assert.Equal(0, changes);
        Assert.False(ctrl.IsFinished);
    }

    [Fact]
    public void Back_from_the_work_objects_returns_to_the_commands()
    {
        var (ctrl, _) = AtTargets();

        ctrl.Handle(MenuInputAction.Back);

        Assert.Equal(CommandLevel.Commands, ctrl.Level);
        Assert.Equal("Command", ctrl.Rows.Selected!.Text);
    }

    [Fact]
    public void No_work_objects_says_there_is_nothing_to_work_on()
    {
        var (ctrl, provider) = Make("a");
        provider.JobTargets.Clear();

        ctrl.Handle(MenuInputAction.Accept);
        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(SettlerCommandController.NoTargetsText, ctrl.DetailLines.Single().Text);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public void No_settlers_says_so()
    {
        var (ctrl, provider) = Make();
        provider.SettlerList.Clear();
        provider.Raise();

        Assert.Equal(SettlerCommandController.NoSettlersText, ctrl.DetailLines.Single().Text);
    }

    [Fact]
    public void Move_hands_a_picker_to_the_host_that_leaves_out_the_current_home_and_reports_the_change()
    {
        var (ctrl, provider) = Make("a");
        DestinationPickerController? picker = null;
        var changes = new List<(string, SettlerChange)>();
        ctrl.PickerRequested += p => picker = p;
        ctrl.SettlerChanged += (id, change) => changes.Add((id, change));
        ctrl.Handle(MenuInputAction.Down);

        ctrl.Handle(MenuInputAction.Accept);

        Assert.NotNull(picker);
        Assert.Equal(new[] { "Hill" }, WorkshopTestHelpers.RowTexts(picker!.List));
        picker.Handle(MenuInputAction.Accept);
        picker.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "home:a>s2" }, provider.Calls);
        Assert.Equal(new[] { ("a", SettlerChange.Home) }, changes);
    }

    [Fact]
    public void Caravan_hands_a_picker_to_the_host_and_assigns_the_route()
    {
        var (ctrl, provider) = Make("a");
        DestinationPickerController? picker = null;
        var changes = new List<SettlerChange>();
        ctrl.PickerRequested += p => picker = p;
        ctrl.SettlerChanged += (_, change) => changes.Add(change);
        Press(ctrl, MenuInputAction.Down, MenuInputAction.Down, MenuInputAction.Accept);

        Assert.NotNull(picker);
        picker!.Handle(MenuInputAction.Accept);
        picker.Handle(MenuInputAction.Accept);

        Assert.Equal(new[] { "route:a>s2" }, provider.Calls);
        Assert.Equal(new[] { SettlerChange.Route }, changes);
    }

    [Fact]
    public void A_picker_command_with_no_host_listening_is_refused()
    {
        var (ctrl, provider) = Make("a");

        Press(ctrl, MenuInputAction.Down, MenuInputAction.Accept);

        Assert.Equal(SettlerCommandController.NotAvailableText, ctrl.Message);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public void Losing_the_settler_while_open_ends_the_flow()
    {
        var (ctrl, provider) = Make("a");
        var results = new List<bool>();
        ctrl.Finished += results.Add;

        provider.SettlerList.RemoveAt(0);
        provider.Raise();

        Assert.True(ctrl.IsFinished);
        Assert.Equal(new[] { false }, results);
    }

    [Fact]
    public void A_provider_change_refreshes_the_settler_list()
    {
        var (ctrl, provider) = Make();
        provider.SettlerList.Add(new SettlerInfo("c", "Cy", "s2"));

        provider.Raise();

        Assert.Equal(new[] { "Ann - Sanctuary", "Bob - Sanctuary", "Cy - Hill" }, Rows(ctrl));
    }

    [Fact]
    public void A_finished_flow_stops_listening_to_the_provider()
    {
        var (ctrl, provider) = Make();
        ctrl.Handle(MenuInputAction.Back);
        var before = Rows(ctrl);

        provider.SettlerList.Add(new SettlerInfo("c", "Cy", "s2"));
        provider.Raise();

        Assert.Equal(before, Rows(ctrl));
    }

    [Fact]
    public void Disposing_the_flow_stops_it_listening_to_the_provider_even_when_it_never_finished()
    {
        var (ctrl, provider) = Make();

        ctrl.Dispose();
        provider.SettlerList.Add(new SettlerInfo("c", "Cy", "s2"));
        provider.Raise();

        Assert.False(ctrl.IsFinished);
        Assert.Equal(new[] { "Ann - Sanctuary", "Bob - Sanctuary" }, Rows(ctrl));
    }

    [Fact]
    public void A_provider_that_throws_on_assign_reports_a_failure_and_stays_usable()
    {
        var (ctrl, provider) = AtTargets();
        var changes = 0;
        ctrl.SettlerChanged += (_, _) => changes++;
        provider.Throw = true;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(SettlerCommandController.FailedText, ctrl.Message);
        Assert.Equal(0, changes);
        Assert.False(ctrl.IsFinished);

        provider.Throw = false;
        ctrl.Handle(MenuInputAction.Accept);

        Assert.True(ctrl.IsFinished);
    }

    [Fact]
    public void A_provider_that_throws_when_listing_work_objects_keeps_the_command_list()
    {
        var (ctrl, provider) = Make("a");
        provider.Throw = true;

        ctrl.Handle(MenuInputAction.Accept);

        Assert.Equal(CommandLevel.Commands, ctrl.Level);
        Assert.Equal(SettlerCommandController.FailedText, ctrl.Message);
    }

    [Fact]
    public void A_provider_that_throws_on_move_fails_inside_the_picker()
    {
        var (ctrl, provider) = Make("a");
        DestinationPickerController? picker = null;
        ctrl.PickerRequested += p => picker = p;
        ctrl.Handle(MenuInputAction.Down);
        ctrl.Handle(MenuInputAction.Accept);
        provider.Throw = true;

        picker!.Handle(MenuInputAction.Accept);
        picker.Handle(MenuInputAction.Accept);

        Assert.Equal(SettlerCommandController.FailedText, picker.Message);
        Assert.False(picker.IsFinished);
    }
}
