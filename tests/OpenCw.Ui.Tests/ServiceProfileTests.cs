using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Service;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ServiceProfileTests
{
    static (ProfilePickerModel Model, InMemoryProfileStore Store) Make(bool inGame = false)
    {
        var store = new InMemoryProfileStore();
        return (new ProfilePickerModel(store, inGame), store);
    }

    static (ProfilePickerModel Model, InMemoryProfileStore Store) WithWork(bool inGame = false)
    {
        var made = Make(inGame);
        Assert.True(made.Model.Create("Work"));
        return made;
    }

    static string Lines(ProfilePickerModel model) => model.DetailText.Replace("\r\n", "\n");

    static string[] Names(ProfilePickerModel model) => model.View.Inner.Rows.Select(p => p.Name).ToArray();

    [Fact]
    public void Name_rule_trims_and_rejects_empty_long_and_control_names()
    {
        Assert.Equal("Enter a profile name.", ProfileNames.Check(""));
        Assert.Equal("Enter a profile name.", ProfileNames.Check("   "));
        Assert.Equal("Enter a profile name.", ProfileNames.Check(null));
        Assert.Equal("Names cannot contain control characters.", ProfileNames.Check("a\u0001b"));
        Assert.Equal("Names are limited to 64 characters.", ProfileNames.Check(new string('a', 65)));
        Assert.Null(ProfileNames.Check(new string('a', 64)));
        Assert.Null(ProfileNames.Check("  padded  "));
        Assert.Equal("padded", ProfileNames.Normalize("  padded  "));
    }

    [Fact]
    public void Name_length_counts_characters_not_utf16_units()
    {
        Assert.Null(ProfileNames.Check(string.Concat(Enumerable.Repeat("\U0001F600", 64))));
        Assert.Equal("Names are limited to 64 characters.", ProfileNames.Check(string.Concat(Enumerable.Repeat("\U0001F600", 65))));
    }

    [Fact]
    public void Lists_the_default_profile_as_active()
    {
        var (model, _) = Make();

        Assert.Equal(new[] { "Default" }, Names(model));
        Assert.Equal("* Default (default) (active)", model.View.RowText(0));
        Assert.Equal("1 profile(s), active: Default", model.Summary);
        Assert.Contains("This is the default profile and cannot be deleted.", model.DetailText);
    }

    [Fact]
    public void Create_selects_the_new_profile_without_activating_it()
    {
        var (model, store) = Make();

        Assert.True(model.Create("  Work  "));

        Assert.Equal(new[] { "Default", "Work" }, Names(model));
        Assert.Equal("Work", model.Selected?.Name);
        Assert.Equal("default", store.ActiveId);
        Assert.Equal("default", model.ActiveId);
        Assert.Empty(store.ChosenIds);
        Assert.Contains("State: not active", model.DetailText);
        Assert.Contains("not active until you choose it", model.Status);
    }

    [Fact]
    public void Create_with_an_invalid_name_changes_nothing()
    {
        var (model, _) = Make();

        Assert.False(model.Create("   "));

        Assert.Equal(new[] { "Default" }, Names(model));
        Assert.Equal("Enter a profile name.", model.Status);
    }

    [Fact]
    public void A_failing_store_shows_a_status_and_keeps_the_list()
    {
        var (model, store) = Make();
        store.FailWrites = true;

        Assert.False(model.Create("Work"));

        Assert.Equal(new[] { "Default" }, Names(model));
        Assert.StartsWith("Create failed:", model.Status);
    }

    [Fact]
    public void Compare_asks_for_a_new_name_and_favourite_asks_to_rename_the_selection()
    {
        var (model, _) = WithWork();
        var requests = new List<NameRequest>();
        model.NameRequested += requests.Add;

        Assert.True(model.View.Handle(MenuInputAction.Compare));
        Assert.True(model.View.Handle(MenuInputAction.Favourite));

        Assert.Equal(new[] { new NameRequest(NameRequestKind.Create, ""), new NameRequest(NameRequestKind.Rename, "Work") }, requests);
    }

    [Fact]
    public void Rename_changes_the_selected_profile_and_keeps_it_selected()
    {
        var (model, _) = WithWork();
        model.RequestRename();

        Assert.True(model.Rename("  Play  "));

        Assert.Equal(new[] { "Default", "Play" }, Names(model));
        Assert.Equal("Play", model.Selected?.Name);
    }

    [Fact]
    public void Rename_without_a_request_or_with_a_bad_name_is_refused()
    {
        var (model, _) = WithWork();

        Assert.False(model.Rename("Play"));

        model.RequestRename();
        Assert.False(model.Rename(new string('x', 65)));
        Assert.Equal(new[] { "Default", "Work" }, Names(model));
        Assert.Contains("limited to 64", model.Status);
    }

    [Fact]
    public void A_failed_rename_reports_the_store_error()
    {
        var (model, store) = WithWork();
        model.RequestRename();
        store.FailWrites = true;

        Assert.False(model.Rename("Play"));

        Assert.StartsWith("Rename failed:", model.Status);
        Assert.Equal(new[] { "Default", "Work" }, Names(model));
    }

    [Fact]
    public void A_cancelled_name_prompt_leaves_nothing_to_rename()
    {
        var (model, _) = WithWork();
        model.RequestRename();

        model.RequestFailed("Could not open the name prompt.");

        Assert.False(model.Rename("Play"));
        Assert.Equal("Could not open the name prompt.", model.Status);
    }

    [Fact]
    public void The_default_profile_cannot_be_deleted()
    {
        var (model, store) = WithWork();
        model.View.Select(0);

        Assert.False(model.RequestDelete());

        Assert.Equal(ProfilePending.None, model.Pending);
        Assert.Contains("default profile cannot be deleted", model.Status);
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void The_active_profile_cannot_be_deleted()
    {
        var (model, _) = WithWork();
        model.Choose();

        Assert.False(model.RequestDelete());

        Assert.Equal(ProfilePending.None, model.Pending);
        Assert.Contains("active profile cannot be deleted", model.Status);
    }

    [Fact]
    public void Delete_waits_for_confirmation_then_moves_the_profile_to_deleted()
    {
        var (model, store) = WithWork();
        var asked = new List<ConfirmRequest>();
        model.ConfirmRequested += asked.Add;

        Assert.True(model.View.Handle(MenuInputAction.TabNext));

        Assert.Equal(ProfilePending.Delete, model.Pending);
        Assert.Equal("Delete profile", Assert.Single(asked).Title);
        Assert.Equal(2, store.List().Count);

        Assert.True(model.View.Handle(MenuInputAction.Accept));

        Assert.Equal(ProfilePending.None, model.Pending);
        Assert.Equal(new[] { "Default" }, Names(model));
        Assert.Equal(new[] { "Work" }, store.Deleted().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Back_cancels_a_pending_delete_and_other_input_is_swallowed_meanwhile()
    {
        var (model, store) = WithWork();
        model.View.Handle(MenuInputAction.TabNext);

        Assert.True(model.View.Handle(MenuInputAction.Down));
        Assert.True(model.View.Handle(MenuInputAction.Compare));
        Assert.Equal(ProfilePending.Delete, model.Pending);

        Assert.True(model.View.Handle(MenuInputAction.Back));

        Assert.Equal(ProfilePending.None, model.Pending);
        Assert.Equal("Cancelled.", model.Status);
        Assert.Equal(2, store.List().Count);
        Assert.Empty(store.Deleted());
    }

    [Fact]
    public void Hints_switch_to_confirm_and_cancel_while_a_question_is_open()
    {
        var (model, _) = WithWork();
        model.RequestDelete();

        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Accept && h.Label == "Confirm");
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Back && h.Label == "Cancel");
    }

    [Fact]
    public void Deleted_view_lists_deleted_profiles_and_accept_restores_one()
    {
        var (model, store) = WithWork();
        model.View.Handle(MenuInputAction.TabNext);
        model.View.Handle(MenuInputAction.Accept);

        model.View.Handle(MenuInputAction.TabPrev);

        Assert.True(model.ShowingDeleted);
        Assert.Equal(new[] { "Work" }, Names(model));
        Assert.Equal("Work (deleted)", model.View.RowText(0));

        Assert.True(model.View.Handle(MenuInputAction.Accept));

        Assert.Empty(model.View.Inner.Rows);
        Assert.Empty(store.Deleted());
        Assert.Equal(new[] { "Default", "Work" }, store.List().Select(p => p.Name).ToArray());
        Assert.Equal("No deleted profiles.", model.DetailText.Split('\n')[0].TrimEnd());
    }

    [Fact]
    public void Back_leaves_the_deleted_view_before_it_closes_the_menu()
    {
        var (model, _) = WithWork();
        model.View.Handle(MenuInputAction.TabPrev);
        Assert.True(model.ShowingDeleted);

        Assert.True(model.View.Handle(MenuInputAction.Back));
        Assert.False(model.ShowingDeleted);

        Assert.False(model.View.Handle(MenuInputAction.Back));
    }

    [Fact]
    public void Create_and_delete_are_not_offered_in_the_deleted_view()
    {
        var (model, _) = WithWork();
        model.View.Handle(MenuInputAction.TabPrev);
        var asked = 0;
        model.NameRequested += _ => asked++;

        Assert.False(model.RequestCreate());
        Assert.False(model.RequestRename());
        Assert.False(model.RequestDelete());

        Assert.Equal(0, asked);
    }

    [Fact]
    public void Choosing_a_profile_makes_it_active_and_reports_it()
    {
        var (model, store) = WithWork();
        var chosen = new List<string>();
        model.ProfileChosen += p => chosen.Add(p.Name);

        Assert.True(model.View.Handle(MenuInputAction.Accept));

        Assert.Equal("p1", store.ActiveId);
        Assert.Equal(new[] { "p1" }, store.ChosenIds);
        Assert.Equal(new[] { "Work" }, chosen);
        Assert.Equal("  Default (default)", model.View.RowText(0));
        Assert.Equal("* Work (active)", model.View.RowText(1));
    }

    [Fact]
    public void Choosing_the_already_active_profile_does_nothing()
    {
        var (model, store) = Make();
        var chosen = 0;
        model.ProfileChosen += _ => chosen++;

        Assert.False(model.Choose());

        Assert.Equal(0, chosen);
        Assert.Empty(store.ChosenIds);
        Assert.Contains("already the active profile", model.Status);
    }

    [Fact]
    public void A_store_that_cannot_switch_keeps_the_old_profile_and_says_so()
    {
        var (model, store) = WithWork();
        store.FailWrites = true;
        var chosen = 0;
        model.ProfileChosen += _ => chosen++;

        Assert.False(model.Choose());

        Assert.Equal(0, chosen);
        Assert.Equal("default", store.ActiveId);
        Assert.StartsWith("Could not switch profile:", model.Status);
    }

    [Fact]
    public void Switching_in_game_asks_first_and_cancel_keeps_the_profile()
    {
        var (model, store) = WithWork(inGame: true);
        var asked = new List<ConfirmRequest>();
        model.ConfirmRequested += asked.Add;

        Assert.True(model.Choose());

        Assert.Equal(ProfilePending.Switch, model.Pending);
        Assert.Equal("Switch profile", Assert.Single(asked).Title);
        Assert.Equal("default", store.ActiveId);

        model.View.Handle(MenuInputAction.Back);

        Assert.Equal(ProfilePending.None, model.Pending);
        Assert.Equal("default", store.ActiveId);
        Assert.Empty(store.ChosenIds);
    }

    [Fact]
    public void Confirming_an_in_game_switch_activates_the_profile_and_reports_it()
    {
        var (model, store) = WithWork(inGame: true);
        var chosen = new List<string>();
        model.ProfileChosen += p => chosen.Add(p.Id);
        model.Choose();

        Assert.True(model.View.Handle(MenuInputAction.Accept));

        Assert.Equal("p1", store.ActiveId);
        Assert.Equal(new[] { "p1" }, chosen);
    }

    [Fact]
    public void A_failed_host_switch_puts_the_previous_profile_back()
    {
        var (model, store) = WithWork();
        model.ProfileChosen += _ => model.SwitchFailed("The game could not load the profile.");

        model.Choose();

        Assert.Equal("default", store.ActiveId);
        Assert.Equal(new[] { "p1", "default" }, store.ChosenIds);
        Assert.Contains("The game could not load the profile.", model.Status);
        Assert.Contains("previous profile is still active", model.Status);
        Assert.Equal("* Default (default) (active)", model.View.RowText(0));
    }

    [Fact]
    public void A_failed_host_switch_reports_when_the_previous_profile_cannot_be_restored()
    {
        var (model, store) = WithWork();
        model.ProfileChosen += _ =>
        {
            store.FailWrites = true;
            model.SwitchFailed("The game could not load the profile.");
        };

        model.Choose();

        Assert.Equal("p1", store.ActiveId);
        Assert.Contains("previous profile could not be restored", model.Status);
    }

    [Fact]
    public void Detail_text_is_the_exact_name_state_and_status_lines()
    {
        var (model, _) = Make();
        Assert.Equal("Default\nState: active\nThis is the default profile and cannot be deleted.", Lines(model));

        model.Create("Work");

        Assert.Equal("Work\nState: not active\n\nCreated \"Work\". It is not active until you choose it.", Lines(model));
    }

    [Fact]
    public void Rows_are_marked_active_default_and_inactive_with_exact_text()
    {
        var (model, _) = WithWork();

        Assert.Equal("* Default (default) (active)", model.View.RowText(0));
        Assert.Equal("  Work", model.View.RowText(1));
        Assert.Equal(string.Empty, model.View.RowText(2));
        Assert.Equal(string.Empty, model.View.RowText(-1));
        Assert.Equal("2 profile(s), active: Default", model.Summary);
    }

    [Fact]
    public void The_deleted_view_has_its_own_summary_detail_and_hints()
    {
        var (model, _) = WithWork();
        model.View.Handle(MenuInputAction.TabNext);
        model.View.Handle(MenuInputAction.Accept);
        model.View.Handle(MenuInputAction.TabPrev);

        Assert.Equal("1 deleted profile(s)", model.Summary);
        Assert.Equal("Work\nState: deleted (can be restored)", Lines(model));
        Assert.Equal(new[]
        {
            (MenuInputAction.Accept, "Restore"),
            (MenuInputAction.Back, "Show profiles"),
            (MenuInputAction.Search, "Search"),
            (MenuInputAction.TabPrev, "Show profiles"),
        }, model.View.Hints.Select(h => (h.Action, h.Label)).ToArray());
    }

    [Fact]
    public void The_normal_and_confirming_hint_lists_are_exact()
    {
        var (model, _) = WithWork();

        Assert.Equal(new[]
        {
            (MenuInputAction.Accept, "Choose"),
            (MenuInputAction.Back, "Back"),
            (MenuInputAction.Search, "Search"),
            (MenuInputAction.Compare, "New profile"),
            (MenuInputAction.Favourite, "Rename"),
            (MenuInputAction.TabNext, "Delete"),
            (MenuInputAction.TabPrev, "Show deleted"),
        }, model.View.Hints.Select(h => (h.Action, h.Label)).ToArray());

        model.RequestDelete();

        Assert.Equal(new[] { (MenuInputAction.Accept, "Confirm"), (MenuInputAction.Back, "Cancel") },
            model.View.Hints.Select(h => (h.Action, h.Label)).ToArray());
    }

    [Fact]
    public void Hints_label_the_service_actions()
    {
        var (model, _) = Make();

        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Accept && h.Label == "Choose");
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Compare && h.Label == "New profile");
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Favourite && h.Label == "Rename");
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.TabNext && h.Label == "Delete");
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.TabPrev && h.Label == "Show deleted");
    }

    [Fact]
    public void Switching_is_refused_before_any_question_when_no_switch_handler_exists()
    {
        var (model, store) = WithWork(inGame: true);
        var asked = 0;
        model.ConfirmRequested += _ => asked++;
        model.CanSwitch = () => false;
        model.View.Handle(MenuInputAction.Down);

        Assert.True(model.View.Handle(MenuInputAction.Accept));

        Assert.Equal(0, asked);
        Assert.Equal(ProfilePending.None, model.Pending);
        Assert.Equal(ProfilePickerModel.SwitchUnavailable, model.Status);
        Assert.Equal("default", store.ActiveId);
    }

    [Fact]
    public void Switching_outside_the_game_is_also_refused_without_touching_the_store()
    {
        var (model, store) = WithWork();
        model.CanSwitch = () => false;
        model.View.Handle(MenuInputAction.Down);

        model.View.Handle(MenuInputAction.Accept);

        Assert.Equal(ProfilePickerModel.SwitchUnavailable, model.Status);
        Assert.Equal("default", store.ActiveId);
    }

    [Fact]
    public void The_switch_message_does_not_read_as_a_placeholder()
    {
        Assert.DoesNotContain("yet", ProfilePickerModel.SwitchUnavailable);
        Assert.DoesNotContain("not connected", ProfilePickerModel.SwitchUnavailable);
    }

    [Fact]
    public void A_confirmation_that_cannot_be_queued_says_another_dialog_is_open()
    {
        var (model, _) = WithWork(inGame: true);
        model.ConfirmRequested += _ => model.CancelPending(ServiceText.DialogBusy);
        model.View.Handle(MenuInputAction.Down);

        model.View.Handle(MenuInputAction.Accept);

        Assert.Equal(ProfilePending.None, model.Pending);
        Assert.Equal("Another dialog is open. Try again.", model.Status);
    }

    [Fact]
    public void The_name_hint_is_built_from_the_name_limit()
    {
        Assert.Equal("Profile names are plain text, 1 to " + ProfileNames.MaxLength + " characters.", ProfileNames.NameHint);
    }
}
