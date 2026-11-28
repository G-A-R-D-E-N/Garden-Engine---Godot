using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Service;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ServiceSetupTests
{
    static InMemorySetupSteps Steps(params string[] items) =>
        new(items.Length == 0 ? new[] { "a", "b", "c" } : items);

    static void Confirm(SetupModel model, int index)
    {
        Assert.True(model.View.Select(index));
        model.View.Handle(MenuInputAction.Accept);
    }

    static SetupModel Ready(InMemorySetupSteps steps)
    {
        var model = new SetupModel(steps);
        Confirm(model, 0);
        Confirm(model, 1);
        return model;
    }

    static SetupModel Running(InMemorySetupSteps steps)
    {
        var model = Ready(steps);
        Confirm(model, 2);
        Assert.True(model.IsRunning);
        return model;
    }

    static void Drain(SetupModel model)
    {
        var guard = 0;
        while (model.Pump())
            Assert.True(++guard < 10000);
    }

    [Fact]
    public void Lists_the_steps_with_the_detected_values()
    {
        var model = new SetupModel(Steps());

        Assert.Equal(3, model.Rows.Count);
        Assert.Equal("Interface.ba2", model.Rows[0].Value);
        Assert.Equal("Converted", model.Rows[1].Value);
        Assert.Equal("1. Game interface archive - not done", model.View.RowText(0));
        Assert.Equal("0 of 3 steps done", model.Summary);
        Assert.Empty(model.View.Tabs);
    }

    [Fact]
    public void A_step_cannot_start_before_the_earlier_ones_are_done()
    {
        var steps = Steps();
        var model = new SetupModel(steps);

        Assert.True(model.View.Select(2));
        Assert.True(model.View.Handle(MenuInputAction.Accept));

        Assert.False(model.IsRunning);
        Assert.Equal(0, steps.StartCalls);
        Assert.Contains("Finish \"Game interface archive\" first.", model.Status);
    }

    [Fact]
    public void Confirming_a_valid_step_marks_it_done_and_unlocks_the_next()
    {
        var model = new SetupModel(Steps());

        Confirm(model, 0);

        Assert.Equal(SetupStepState.Done, model.Rows[0].State);
        Assert.Equal("1. Game interface archive - done", model.View.RowText(0));
        Assert.Equal("1 of 3 steps done", model.Summary);

        Confirm(model, 1);
        Assert.Equal(SetupStepState.Done, model.Rows[1].State);
    }

    [Fact]
    public void Mouse_activation_confirms_the_clicked_step()
    {
        var model = new SetupModel(Steps());

        Assert.True(model.View.Activate(0));

        Assert.Equal(SetupStepState.Done, model.Rows[0].State);
    }

    [Fact]
    public void An_invalid_detected_path_blocks_the_step_and_everything_after_it()
    {
        var steps = new InMemorySetupSteps(new[] { "a" }) { ArchiveDetected = "Interface.zip" };
        var model = new SetupModel(steps);

        Confirm(model, 0);

        Assert.Equal(SetupStepState.Invalid, model.Rows[0].State);
        Assert.Equal("The archive path must end in .ba2.", model.Rows[0].Message);
        Assert.Equal("1. Game interface archive - needs attention", model.View.RowText(0));

        Confirm(model, 1);
        Assert.Equal(SetupStepState.Pending, model.Rows[1].State);
        Assert.Contains("Finish \"Game interface archive\" first.", model.Status);
    }

    [Fact]
    public void Editing_the_value_fixes_an_invalid_step()
    {
        var steps = new InMemorySetupSteps(new[] { "a" }) { ArchiveDetected = "Interface.zip" };
        var model = new SetupModel(steps);
        Confirm(model, 0);
        var asked = new List<SetupStepRow>();
        model.ValueRequested += asked.Add;
        model.View.Select(0);

        Assert.True(model.View.Handle(MenuInputAction.Compare));
        Assert.Same(model.Rows[0], Assert.Single(asked));

        Assert.True(model.SetValue("  Other.ba2 "));

        Assert.Equal("Other.ba2", model.Rows[0].Value);
        Assert.Equal(SetupStepState.Pending, model.Rows[0].State);
        Confirm(model, 0);
        Assert.Equal(SetupStepState.Done, model.Rows[0].State);
    }

    [Fact]
    public void Entering_a_bad_value_keeps_the_step_invalid()
    {
        var model = new SetupModel(Steps());
        model.View.Select(0);
        model.RequestValue();

        Assert.False(model.SetValue("Interface.zip"));

        Assert.Equal(SetupStepState.Invalid, model.Rows[0].State);
        Assert.Equal("The archive path must end in .ba2.", model.Status);
    }

    [Fact]
    public void A_value_can_only_be_edited_on_editable_steps_and_never_set_unasked()
    {
        var model = new SetupModel(Steps());
        var asked = 0;
        model.ValueRequested += _ => asked++;
        model.View.Select(2);

        Assert.False(model.RequestValue());
        Assert.False(model.SetValue("Other.ba2"));

        Assert.Equal(0, asked);
        Assert.Equal("Interface.ba2", model.Rows[0].Value);
    }

    [Fact]
    public void Changing_an_earlier_value_sends_later_done_steps_back_to_pending()
    {
        var model = Ready(Steps());
        model.View.Select(0);
        model.RequestValue();

        model.SetValue("Other.ba2");

        Assert.Equal(SetupStepState.Pending, model.Rows[0].State);
        Assert.Equal(SetupStepState.Pending, model.Rows[1].State);
    }

    [Fact]
    public void A_run_advances_one_item_per_pump_across_two_phases_of_half_the_bar_each()
    {
        var steps = Steps();
        var model = Running(steps);
        var fractions = new List<double>();

        while (model.Pump())
            fractions.Add(model.Fraction);

        Assert.Equal(5, fractions.Count);
        for (var i = 0; i < fractions.Count; i++)
            Assert.Equal((i + 1) / 6.0, fractions[i], 6);
        Assert.Equal(1.0, model.Fraction, 6);
        Assert.False(model.IsRunning);
        Assert.Equal(SetupStepState.Done, model.Rows[2].State);
        Assert.Equal(6, model.Results.Count);
        Assert.All(model.Results, r => Assert.Equal(SetupItemState.Written, r.State));
        Assert.Equal(1, steps.StartCalls);
    }

    [Fact]
    public void The_phase_name_changes_halfway_through()
    {
        var model = Running(Steps());

        model.Pump();
        Assert.Equal("Extracting movies", model.Progress?.Phase);
        Assert.Equal(0, model.Progress?.PhaseIndex);

        model.Pump();
        model.Pump();
        Assert.Equal("Converting art", model.Progress?.Phase);
        Assert.Equal(1, model.Progress?.PhaseIndex);
        Assert.Equal(0.5, model.Fraction, 6);
    }

    [Fact]
    public void Detail_text_shows_the_phase_item_and_a_text_bar_while_running()
    {
        var model = Running(Steps());

        model.Pump();

        var text = model.DetailText;
        Assert.Contains("Phase 1 of 2: Extracting movies, 1 of 3", text);
        Assert.Contains("Item: a", text);
        Assert.Contains("[###-----------------] 17%", text);
        Assert.Equal("3. Extract and convert - running", model.View.RowText(2));
    }

    [Fact]
    public void Finishing_the_last_step_records_setup_complete_once()
    {
        var steps = Steps();
        var model = Running(steps);

        Drain(model);

        Assert.True(model.AllDone);
        Assert.True(steps.IsComplete);
        Assert.Equal(1, steps.CompleteCalls);
        Assert.Equal("Setup complete.", model.Status);
        Assert.Equal("All steps done", model.Summary);
    }

    [Fact]
    public void An_already_complete_setup_is_not_recorded_again()
    {
        var steps = Steps();
        steps.MarkComplete();
        var model = Running(steps);

        Drain(model);

        Assert.Equal(1, steps.CompleteCalls);
    }

    [Fact]
    public void A_setup_that_cannot_be_recorded_says_so()
    {
        var steps = Steps();
        steps.CompleteResult = ServiceResult.Fail("disk full");
        var model = Running(steps);

        Drain(model);

        Assert.True(model.AllDone);
        Assert.False(steps.IsComplete);
        Assert.Equal("Setup finished but could not be recorded: disk full", model.Status);
    }

    [Fact]
    public void Item_errors_do_not_stop_the_run_and_the_other_items_still_complete()
    {
        var steps = Steps();
        steps.FailItems.Add("b");
        var model = Running(steps);

        Drain(model);

        Assert.Equal(SetupStepState.Done, model.Rows[2].State);
        Assert.Equal(2, model.Results.Count(r => r.State == SetupItemState.Error));
        Assert.Equal(4, model.Results.Count(r => r.State == SetupItemState.Written));
        Assert.Equal("Finished with 2 failed item(s); the others completed.", model.Rows[2].Message);
        Assert.Contains("b: Error - Could not read this item.", model.DetailText);
        Assert.Equal(1, steps.CompleteCalls);
    }

    [Fact]
    public void Cancel_stops_before_the_next_item_and_keeps_the_finished_ones()
    {
        var steps = Steps();
        var model = Running(steps);
        model.Pump();

        Assert.True(model.View.Handle(MenuInputAction.Favourite));
        Assert.True(model.IsStopping);
        Assert.False(model.Pump());

        Assert.False(model.IsRunning);
        Assert.False(model.IsStopping);
        Assert.Equal(SetupStepState.Cancelled, model.Rows[2].State);
        Assert.Equal("Cancelled. Choose this step again to resume.", model.Rows[2].Message);
        Assert.Equal(new[] { "a" }, model.Results.Select(r => r.Item).ToArray());
        Assert.Equal(0, steps.CompleteCalls);
        Assert.Equal("2 of 3 steps done", model.Summary);
    }

    [Fact]
    public void A_second_run_cannot_start_while_the_first_is_stopping()
    {
        var steps = Steps();
        var model = Running(steps);
        model.Cancel();

        model.View.Handle(MenuInputAction.Accept);

        Assert.Equal("Wait for the current run to stop.", model.Status);
        Assert.Equal(1, steps.StartCalls);
        Assert.False(model.Cancel());
    }

    [Fact]
    public void Accept_while_a_step_is_running_is_refused()
    {
        var steps = Steps();
        var model = Running(steps);

        model.View.Handle(MenuInputAction.Accept);

        Assert.Equal("A step is already running.", model.Status);
        Assert.Equal(1, steps.StartCalls);
        Assert.False(model.RequestValue());
    }

    [Fact]
    public void A_cancelled_step_can_be_started_again_and_finish()
    {
        var steps = Steps();
        var model = Running(steps);
        model.Pump();
        model.Cancel();
        model.Pump();
        model.View.Select(2);
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Accept && h.Label == "Resume");

        model.View.Handle(MenuInputAction.Accept);
        Assert.True(model.IsRunning);
        Assert.Equal(2, steps.StartCalls);
        Drain(model);

        Assert.Equal(SetupStepState.Done, model.Rows[2].State);
        Assert.Equal(1, steps.CompleteCalls);
    }

    [Fact]
    public void Back_cancels_a_running_step_and_lets_the_menu_close()
    {
        var model = Running(Steps());
        model.Pump();

        Assert.False(model.View.Handle(MenuInputAction.Back));

        Assert.True(model.IsStopping);
        model.Pump();
        Assert.Equal(SetupStepState.Cancelled, model.Rows[2].State);
    }

    [Fact]
    public void Back_with_nothing_running_just_closes()
    {
        var model = Ready(Steps());

        Assert.False(model.View.Handle(MenuInputAction.Back));

        Assert.False(model.IsStopping);
        Assert.False(model.Cancel());
    }

    [Fact]
    public void Pump_does_nothing_without_a_run()
    {
        var model = new SetupModel(Steps());

        Assert.False(model.Pump());
        Assert.Equal(0, model.Fraction);
    }

    [Fact]
    public void A_run_over_no_items_finishes_on_the_first_pump()
    {
        var steps = new InMemorySetupSteps(new List<string>());
        var model = Running(steps);

        Assert.False(model.Pump());

        Assert.Equal(SetupStepState.Done, model.Rows[2].State);
        Assert.Empty(model.Results);
    }

    [Fact]
    public void A_cancel_after_the_run_finished_does_not_mark_it_cancelled()
    {
        var run = Steps("a").Start(InMemorySetupSteps.RunStep, string.Empty)!;
        while (run.Advance())
        {
        }

        run.Cancel();

        Assert.True(run.Finished);
        Assert.False(run.Cancelled);
    }

    [Fact]
    public void Long_result_lists_show_only_the_newest_lines()
    {
        var items = Enumerable.Range(0, 250).Select(i => "i" + i).ToArray();
        var model = Running(new InMemorySetupSteps(items));

        Drain(model);

        var text = model.DetailText;
        Assert.Contains("Results (500):", text);
        Assert.Contains("... 300 earlier result(s) not shown", text);
        Assert.Contains("  i50: Written", text);
        Assert.DoesNotContain("  i49: Written", text);
        Assert.Equal(500, model.Results.Count);
    }

    [Fact]
    public void Hints_follow_the_selected_step_and_the_run_state()
    {
        var model = new SetupModel(Steps());

        model.View.Select(0);
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Accept && h.Label == "Confirm");
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Compare && h.Label == "Edit value");

        model.View.Select(2);
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Accept && h.Label == "Run");
        Assert.DoesNotContain(model.View.Hints, h => h.Action == MenuInputAction.Compare);

        Confirm(model, 0);
        Confirm(model, 1);
        Confirm(model, 2);
        Assert.Contains(model.View.Hints, h => h.Action == MenuInputAction.Favourite && h.Label == "Cancel run");
    }

    [Fact]
    public void Text_bar_clamps_and_rounds()
    {
        Assert.Equal("[--------------------] 0%", SetupModel.Bar(0));
        Assert.Equal("[##########----------] 50%", SetupModel.Bar(0.5));
        Assert.Equal("[####################] 100%", SetupModel.Bar(2));
        Assert.Equal("[--------------------] 0%", SetupModel.Bar(-1));
    }

    [Fact]
    public void Progress_fraction_spreads_the_phases_over_the_whole_bar()
    {
        Assert.Equal(0.5, new SetupProgress("p", 1, 2, 0, 0, "").Fraction, 6);
        Assert.Equal(0.25, new SetupProgress("p", 0, 2, 1, 2, "").Fraction, 6);
        Assert.Equal(1.0, new SetupProgress("p", 1, 2, 9, 3, "").Fraction, 6);
        Assert.Equal(0.0, new SetupProgress("p", 0, 0, 1, 1, "").Fraction, 6);
    }

    sealed class StuckSteps : ISetupSteps
    {
        readonly InMemorySetupSteps _inner = new(new[] { "a" });

        public IReadOnlyList<SetupStepInfo> Steps => _inner.Steps;

        public bool IsComplete => false;

        public string Detect(string stepId) => _inner.Detect(stepId);

        public SetupValidation Validate(string stepId, string value) => _inner.Validate(stepId, value);

        public ISetupRun? Start(string stepId, string value) => new StuckRun();

        public ServiceResult MarkComplete() => _inner.MarkComplete();
    }

    sealed class StuckRun : ISetupRun
    {
        public SetupProgress Progress { get; } = new("Stuck", 0, 1, 0, 1, string.Empty);

        public bool Finished => false;

        public bool Cancelled => false;

        public IReadOnlyList<SetupItemResult> Results { get; } = new List<SetupItemResult>();

        public bool Advance() => true;

        public void Cancel()
        {
        }
    }

    [Fact]
    public void A_completed_setup_starts_every_row_done()
    {
        var steps = Steps();
        steps.MarkComplete();

        var model = new SetupModel(steps);

        Assert.True(model.AllDone);
        Assert.Equal("All steps done", model.Summary);
        Assert.Equal("1. Game interface archive - done", model.View.RowText(0));
        Assert.Equal("3. Extract and convert - done", model.View.RowText(2));
    }

    [Fact]
    public void An_unfinished_setup_still_starts_every_row_pending()
    {
        var model = new SetupModel(Steps());

        Assert.All(model.Rows, r => Assert.Equal(SetupStepState.Pending, r.State));
    }

    [Fact]
    public void Abort_finishes_a_running_setup_without_another_pump()
    {
        var model = Running(Steps());
        model.Pump();

        Assert.True(model.Abort());

        Assert.False(model.IsRunning);
        Assert.False(model.IsStopping);
        Assert.Equal(SetupStepState.Cancelled, model.Rows[2].State);
        Assert.Equal("Stopped. Finished items are kept.", model.Status);
        Assert.Equal(new[] { new SetupItemResult("a", SetupItemState.Written) }, model.Results);
    }

    [Fact]
    public void Abort_ends_a_run_that_ignores_cancel()
    {
        var model = new SetupModel(new StuckSteps());
        Confirm(model, 0);
        Confirm(model, 1);
        Confirm(model, 2);
        Assert.True(model.IsRunning);

        Assert.True(model.Abort());

        Assert.False(model.IsRunning);
        Assert.Equal(SetupStepState.Cancelled, model.Rows[2].State);
    }

    [Fact]
    public void Abort_does_nothing_when_no_run_is_active()
    {
        var model = new SetupModel(Steps());

        Assert.False(model.Abort());
        Assert.Equal(SetupStepState.Pending, model.Rows[0].State);
    }

    [Fact]
    public void A_cancelled_value_request_clears_the_pending_row_and_can_say_why()
    {
        var model = new SetupModel(Steps());
        model.View.Select(0);
        model.ValueRequested += _ => model.CancelValue(ServiceText.DialogBusy);

        Assert.False(model.RequestValue());

        Assert.Equal("Another dialog is open. Try again.", model.Status);
        Assert.False(model.SetValue("Other.ba2"));
        Assert.Equal("Interface.ba2", model.Rows[0].Value);
    }

    [Fact]
    public void Dismissing_the_value_prompt_clears_the_pending_row_silently()
    {
        var model = new SetupModel(Steps());
        model.View.Select(0);
        Assert.True(model.RequestValue());

        model.CancelValue();

        Assert.False(model.SetValue("Other.ba2"));
        Assert.Equal(string.Empty, model.Status);
    }

    [Fact]
    public void Editable_steps_carry_the_value_length_limit()
    {
        var model = new SetupModel(Steps());

        Assert.All(model.Rows.Where(r => r.Info.Editable), r => Assert.Equal(SetupStepInfo.DefaultMaxLength, r.Info.MaxLength));
        Assert.Equal(260, SetupStepInfo.DefaultMaxLength);
    }
}
