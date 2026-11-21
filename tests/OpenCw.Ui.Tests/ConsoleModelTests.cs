using OpenCommonwealth.Services.Interaction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenCommonwealth.Services.Ui.DevConsole;
using OpenCommonwealth.Services.Ui.DevConsole.DebugChecks;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ConsoleModelTests
{
    sealed class ScriptedHost : IConsoleHost
    {
        public Func<string, ConsoleTarget?, ConsoleResult> OnExecute { get; set; } = (_, _) => ConsoleResult.Info("ok");
        public Func<string, IReadOnlyList<string>> OnComplete { get; set; } = _ => Array.Empty<string>();
        public List<string> Prefixes { get; } = new();

        public ConsoleResult Execute(string line, ConsoleTarget? target) => OnExecute(line, target);

        public IReadOnlyList<string> Complete(string prefix)
        {
            Prefixes.Add(prefix);
            return OnComplete(prefix);
        }

        public ConsoleTarget? Pick(double x, double y) => null;
    }

    static (ConsoleModel Model, InMemoryConsoleHost Host) Make()
    {
        var host = new InMemoryConsoleHost().AddTarget(new ConsoleTarget("a", "Alpha"), 100, 100, 10);
        return (new ConsoleModel(host), host);
    }

    static void Run(ConsoleModel model, string text)
    {
        model.Input.Insert(text);
        Assert.True(model.Submit());
        model.Tick(0);
    }

    [Fact]
    public void Submit_echoes_at_once_and_the_host_runs_on_the_next_tick()
    {
        var (model, host) = Make();
        model.Input.Insert("echo hi");

        Assert.True(model.Submit());

        Assert.Equal(new[] { new ConsoleLine(ConsoleSeverity.Echo, "> echo hi") }, model.Lines);
        Assert.Empty(host.Executed);
        Assert.Equal(string.Empty, model.Input.Value);

        model.Tick(0);

        Assert.Equal(
            new[] { new ConsoleLine(ConsoleSeverity.Echo, "> echo hi"), new ConsoleLine(ConsoleSeverity.Info, "hi") },
            model.Lines);
        Assert.Equal("echo hi", host.Executed.Single().Line);
    }

    [Fact]
    public void An_empty_or_blank_submission_is_ignored()
    {
        var (model, _) = Make();
        model.Input.Insert("   ");

        Assert.False(model.Submit());

        Assert.Empty(model.Lines);
        Assert.Empty(model.History);
    }

    [Fact]
    public void Help_prints_the_host_list_without_reaching_the_host_and_is_case_insensitive()
    {
        var (model, host) = Make();

        Run(model, "HeLp");

        Assert.Equal(
            new[] { new ConsoleLine(ConsoleSeverity.Echo, "> HeLp"), new ConsoleLine(ConsoleSeverity.Info, "echo, get, listrefs, prid, set") },
            model.Lines);
        Assert.Empty(host.Executed);
    }

    [Fact]
    public void Help_with_no_host_commands_says_so()
    {
        var model = new ConsoleModel(new ScriptedHost());

        Run(model, "help");

        Assert.Equal(new ConsoleLine(ConsoleSeverity.Info, ConsoleModel.NoCommands), model.Lines[^1]);
    }

    [Fact]
    public void Clear_empties_the_output_but_keeps_the_command_history()
    {
        var (model, host) = Make();
        Run(model, "echo one");

        Run(model, "CLEAR");

        Assert.Empty(model.Lines);
        Assert.Equal(new[] { "echo one", "CLEAR" }, model.History);
        Assert.Single(host.Executed);
    }

    [Fact]
    public void Up_and_down_browse_history_and_down_past_the_newest_restores_the_draft()
    {
        var (model, _) = Make();
        Run(model, "echo a");
        Run(model, "echo b");
        Run(model, "echo c");
        model.Input.Insert("dr");

        Assert.True(model.Handle(MenuInputAction.Up));
        Assert.Equal("echo c", model.Input.Value);
        model.Handle(MenuInputAction.Up);
        Assert.Equal("echo b", model.Input.Value);
        model.Handle(MenuInputAction.Down);
        Assert.Equal("echo c", model.Input.Value);
        model.Handle(MenuInputAction.Down);
        Assert.Equal("dr", model.Input.Value);
        Assert.False(model.HistoryNext());
    }

    [Fact]
    public void Up_at_the_oldest_line_stays_there_and_an_empty_history_does_nothing()
    {
        var (model, _) = Make();
        Assert.False(model.HistoryPrevious());
        Run(model, "echo a");
        Run(model, "echo b");

        model.HistoryPrevious();
        model.HistoryPrevious();
        model.HistoryPrevious();

        Assert.Equal("echo a", model.Input.Value);
    }

    [Fact]
    public void A_resubmitted_line_moves_to_the_newest_slot_and_history_keeps_fifty()
    {
        var (model, _) = Make();
        Run(model, "echo a");
        Run(model, "echo b");
        Run(model, "echo a");

        Assert.Equal(new[] { "echo b", "echo a" }, model.History);

        for (var i = 0; i < 60; i++)
            Run(model, "echo l" + i);

        Assert.Equal(ConsoleModel.MaxHistory, model.History.Count);
        Assert.Equal("echo l10", model.History[0]);
        Assert.Equal("echo l59", model.History[^1]);
    }

    [Fact]
    public void A_throwing_host_adds_an_error_line_and_the_console_stays_usable()
    {
        var host = new ScriptedHost
        {
            OnExecute = (line, _) => line == "bad" ? throw new InvalidOperationException("boom") : ConsoleResult.Info("fine"),
        };
        var model = new ConsoleModel(host);

        Run(model, "bad");

        Assert.Equal(new ConsoleLine(ConsoleSeverity.Error, "boom"), model.Lines[^1]);
        Assert.True(model.IsOpen);

        Run(model, "good");

        Assert.Equal(new ConsoleLine(ConsoleSeverity.Info, "fine"), model.Lines[^1]);
    }

    [Fact]
    public void Host_severities_are_kept_and_a_returned_target_replaces_the_selection()
    {
        var host = new ScriptedHost
        {
            OnExecute = (_, _) => new ConsoleResult(
                new[] { new ConsoleLine(ConsoleSeverity.Warning, "careful") }, new ConsoleTarget("z", "Zed")),
        };
        var model = new ConsoleModel(host);

        Run(model, "anything");

        Assert.Equal(new ConsoleLine(ConsoleSeverity.Warning, "careful"), model.Lines[^1]);
        Assert.Equal("Target: Zed", model.TargetText);
    }

    [Fact]
    public void Picking_sets_and_clears_the_target_and_the_host_sees_it()
    {
        var (model, host) = Make();
        Assert.Equal("Target: none", model.TargetText);

        model.PickAt(105, 100);

        Assert.Equal("Target: Alpha", model.TargetText);
        Assert.Equal(new ConsoleLine(ConsoleSeverity.Info, "Picked Alpha."), model.Lines[^1]);

        Run(model, "echo x");

        Assert.Equal("a", host.Executed.Single().Target?.Id);

        model.PickAt(0, 0);

        Assert.Equal("Target: none", model.TargetText);
        Assert.Equal(new ConsoleLine(ConsoleSeverity.Info, ConsoleModel.TargetCleared), model.Lines[^1]);
        var count = model.Lines.Count;

        model.PickAt(0, 0);

        Assert.Equal(count, model.Lines.Count);
    }

    [Fact]
    public void The_prid_command_selects_a_reference_for_later_commands()
    {
        var (model, host) = Make();

        Run(model, "prid a");
        Run(model, "echo y");

        Assert.Equal("Target: Alpha", model.TargetText);
        Assert.Equal("a", host.Executed[1].Target?.Id);
    }

    [Fact]
    public void Input_is_cut_at_512_and_a_pasted_newline_becomes_a_space()
    {
        var (model, _) = Make();

        model.Input.Paste(new string('a', 600));

        Assert.Equal(ConsoleModel.MaxInput, model.Input.Value.Length);

        model.EditText(string.Empty, 0);
        model.Input.Paste("a\nb");

        Assert.Equal("a b", model.Input.Value);
    }

    [Fact]
    public void Six_hundred_lines_keep_only_the_last_five_hundred()
    {
        var (model, _) = Make();
        for (var i = 0; i < 600; i++)
            model.Append(ConsoleSeverity.Info, "line " + i);

        model.Tick(0);

        Assert.Equal(ConsoleModel.MaxLines, model.Lines.Count);
        Assert.Equal("line 100", model.Lines[0].Text);
        Assert.Equal("line 599", model.Lines[^1].Text);
    }

    [Fact]
    public void A_thousand_lines_appended_without_a_tick_keep_only_the_newest_five_hundred()
    {
        var (model, _) = Make();
        for (var i = 0; i < 1000; i++)
            model.Append(ConsoleSeverity.Info, "line " + i);

        model.Tick(0);

        Assert.Equal(ConsoleModel.MaxLines, model.Lines.Count);
        Assert.Equal("line 500", model.Lines[0].Text);
        Assert.Equal("line 999", model.Lines[^1].Text);
    }

    [Fact]
    public void Append_from_another_thread_waits_for_the_next_tick_and_strips_control_characters()
    {
        var (model, _) = Make();

        Task.Run(() => model.Append(ConsoleSeverity.Warning, "a\tb\nc")).Wait();

        Assert.Empty(model.Lines);

        model.Tick(0);

        Assert.Equal(new[] { new ConsoleLine(ConsoleSeverity.Warning, "a b c") }, model.Lines);
    }

    [Fact]
    public void Scrolling_up_keeps_the_view_still_when_lines_arrive_and_shows_more_below()
    {
        var (model, _) = Make();
        model.SetViewLines(5);
        for (var i = 0; i < 20; i++)
            model.Append(ConsoleSeverity.Info, "n" + i);
        model.Tick(0);
        Assert.Equal(new[] { "n15", "n16", "n17", "n18", "n19" }, model.VisibleLines.Select(l => l.Text));
        Assert.False(model.HasMoreBelow);

        model.Scroll(3);

        Assert.True(model.HasMoreBelow);
        Assert.Equal(new[] { "n12", "n13", "n14", "n15", "n16" }, model.VisibleLines.Select(l => l.Text));

        model.Append(ConsoleSeverity.Info, "n20");
        model.Tick(0);

        Assert.Equal(new[] { "n12", "n13", "n14", "n15", "n16" }, model.VisibleLines.Select(l => l.Text));
        Assert.Equal(4, model.ScrollOffset);

        model.ScrollToStart();
        Assert.Equal(16, model.ScrollOffset);
        model.Scroll(1000);
        Assert.Equal(16, model.ScrollOffset);
        model.ScrollToEnd();
        Assert.Equal(0, model.ScrollOffset);
        Assert.False(model.HasMoreBelow);
    }

    [Fact]
    public void Page_keys_scroll_one_page_less_one_line_and_never_touch_the_input()
    {
        var (model, _) = Make();
        model.SetViewLines(5);
        for (var i = 0; i < 30; i++)
            model.Append(ConsoleSeverity.Info, "n" + i);
        model.Tick(0);
        model.Input.Insert("abc");

        model.Handle(MenuInputAction.PageUp);

        Assert.Equal(4, model.ScrollOffset);
        Assert.Equal("abc", model.Input.Value);

        model.Handle(MenuInputAction.PageDown);

        Assert.Equal(0, model.ScrollOffset);
    }

    [Fact]
    public void Tab_replaces_the_current_word_with_the_first_match_and_cycles()
    {
        var host = new ScriptedHost
        {
            OnComplete = prefix => prefix == "al" ? new[] { "alpha", "alpine" } : Array.Empty<string>(),
        };
        var model = new ConsoleModel(host);
        model.Input.Insert("x al");

        Assert.True(model.Complete());
        Assert.Equal("x alpha", model.Input.Value);
        Assert.True(model.Handle(MenuInputAction.TabNext));
        Assert.Equal("x alpine", model.Input.Value);
        model.Complete();
        Assert.Equal("x alpha", model.Input.Value);
        Assert.Equal("al", host.Prefixes[0]);
    }

    [Fact]
    public void Tab_with_no_matches_or_no_completion_support_changes_nothing()
    {
        var model = new ConsoleModel(new ScriptedHost());
        model.Input.Insert("zz");

        Assert.False(model.Complete());
        Assert.Equal("zz", model.Input.Value);
    }

    [Fact]
    public void A_command_submitted_and_closed_before_the_next_tick_still_runs()
    {
        var (model, host) = Make();
        var finished = 0;
        model.Finished += () => finished++;
        model.Input.Insert("echo late");
        Assert.True(model.Submit());
        Assert.Empty(host.Executed);

        model.Handle(MenuInputAction.Back);

        Assert.Equal("echo late", host.Executed.Single().Line);
        Assert.Equal(1, finished);
        Assert.False(model.IsOpen);
        Assert.Equal(new ConsoleLine(ConsoleSeverity.Info, "late"), model.Lines[^1]);

        model.Tick(0);

        Assert.Single(host.Executed);
    }

    [Fact]
    public void Several_queued_commands_run_in_order_when_the_console_closes()
    {
        var (model, host) = Make();
        model.Input.Insert("echo one");
        model.Submit();
        model.Input.Insert("echo two");
        model.Submit();

        model.Close();

        Assert.Equal(new[] { "echo one", "echo two" }, host.Executed.Select(call => call.Line));
    }

    [Fact]
    public void A_queued_command_that_closes_the_console_again_does_not_re_enter_or_finish_twice()
    {
        var host = new ScriptedHost();
        var model = new ConsoleModel(host);
        var finished = 0;
        model.Finished += () => finished++;
        host.OnExecute = (_, _) =>
        {
            model.Close();
            return ConsoleResult.Info("ok");
        };
        model.Input.Insert("anything");
        model.Submit();

        model.Close();

        Assert.Equal(1, finished);
    }

    [Fact]
    public void Back_closes_once_and_reopening_clears_the_input_but_keeps_lines_history_and_target()
    {
        var (model, _) = Make();
        var finished = 0;
        model.Finished += () => finished++;
        model.PickAt(100, 100);
        Run(model, "echo keep");
        model.Input.Insert("draft");
        model.SetViewLines(1);
        model.Scroll(1);

        model.Handle(MenuInputAction.Back);
        model.Handle(MenuInputAction.Back);

        Assert.Equal(1, finished);
        Assert.False(model.IsOpen);
        Assert.False(model.Submit());

        model.Open();

        Assert.True(model.IsOpen);
        Assert.Equal(string.Empty, model.Input.Value);
        Assert.Equal(0, model.ScrollOffset);
        Assert.Equal(new[] { "echo keep" }, model.History);
        Assert.Equal("Target: Alpha", model.TargetText);
        Assert.Equal(
            new[]
            {
                new ConsoleLine(ConsoleSeverity.Info, "Picked Alpha."),
                new ConsoleLine(ConsoleSeverity.Echo, "> echo keep"),
                new ConsoleLine(ConsoleSeverity.Info, "keep"),
            },
            model.Lines);
    }

    [Fact]
    public void Accept_submits_the_line_typed()
    {
        var (model, host) = Make();
        model.Input.Insert("echo z");

        Assert.True(model.Handle(MenuInputAction.Accept));
        model.Tick(0);

        Assert.Equal("echo z", host.Executed.Single().Line);
    }

    [Fact]
    public void The_in_memory_host_runs_its_sample_commands()
    {
        var host = new InMemoryConsoleHost();

        Assert.Equal("No value named hp", host.Execute("get hp", null).Lines.Single().Text);
        Assert.Equal("hp set to 12", host.Execute("set hp 12", null).Lines.Single().Text);
        Assert.Equal("hp = 12", host.Execute("get hp", null).Lines.Single().Text);
        Assert.Equal(ConsoleSeverity.Error, host.Execute("set hp NaN", null).Lines.Single().Severity);
        Assert.Equal("Unknown command: foo", host.Execute("foo", null).Lines.Single().Text);
        Assert.Equal(new[] { "set" }, host.Complete("s"));
        Assert.Equal(5, host.Complete(string.Empty).Count);
        Assert.Null(host.Pick(1, 1));
    }

    [Fact]
    public void The_companion_console_recruits_reports_and_dismisses_and_leaves_other_commands_to_the_inner_host()
    {
        var roster = new CompanionRoster();
        var model = new ConsoleModel(new CompanionConsoleHost(new InMemoryConsoleHost(), roster));

        Run(model, "companion status");
        Run(model, "companion recruit 00AA:A.esm 0A01:A.esm");
        Run(model, "companion recruit 00AA:A.esm 0A01:A.esm");
        Run(model, "companion recruit 00AA:A.esm");
        Run(model, "echo hi");
        Run(model, "companion dismiss");
        Run(model, "companion dismiss");

        var replies = model.Lines.Where(line => line.Severity != ConsoleSeverity.Echo).ToArray();
        Assert.Equal("No companion.", replies[0].Text);
        Assert.Equal(ConsoleSeverity.Info, replies[1].Severity);
        Assert.Equal("Recruited without the script gate. Companion 00AA:A.esm (base 0A01:A.esm): Follow, Medium, Defensive.", replies[1].Text);
        Assert.Equal(ConsoleSeverity.Error, replies[2].Severity);
        Assert.Equal(CompanionConsoleHost.Usage, replies[3].Text);
        Assert.Equal("hi", replies[4].Text);
        Assert.Equal("Dismissed 00AA:A.esm. No companion.", replies[5].Text);
        Assert.Equal(ConsoleSeverity.Error, replies[6].Severity);
        Assert.Null(roster.Current);
    }

    [Fact]
    public void A_bare_companion_recruit_takes_the_nearest_npc_and_refuses_when_none_is_near()
    {
        var roster = new CompanionRoster();
        (string RefKey, string BaseKey)? near = null;
        var model = new ConsoleModel(new CompanionConsoleHost(new InMemoryConsoleHost(), roster, () => near));

        Run(model, "companion recruit");
        near = ("00BB:A.esm", "0B01:A.esm");
        Run(model, "companion recruit");

        var replies = model.Lines.Where(line => line.Severity != ConsoleSeverity.Echo).ToArray();
        Assert.Equal(ConsoleSeverity.Error, replies[0].Severity);
        Assert.StartsWith("No nearby NPC.", replies[0].Text);
        Assert.Equal(ConsoleSeverity.Info, replies[1].Severity);
        Assert.Equal(new CompanionEntry("00BB:A.esm", "0B01:A.esm", CompanionState.Follow, CompanionDistance.Medium, CompanionStance.Defensive), roster.Current);
    }

    static (ConsoleModel Model, InMemoryConsoleHost Inner, FakeRdfProcess Process) MakeChecks()
    {
        var inner = new InMemoryConsoleHost();
        var process = new FakeRdfProcess();
        ConsoleModel? model = null;
        var host = new ChecksConsoleHost(inner, new RdfClient(process), (severity, text) => model!.Append(severity, text));
        model = new ConsoleModel(host);
        return (model, inner, process);
    }

    static void WaitFor(ConsoleModel model, string text)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!model.Lines.Any(l => l.Text.Contains(text)) && DateTime.UtcNow < until)
        {
            model.Tick(0);
            System.Threading.Thread.Sleep(5);
        }
        Assert.Contains(model.Lines, l => l.Text.Contains(text));
    }

    [Fact]
    public void Checks_run_streams_each_test_then_the_launcher_verdict_and_the_failing_names_without_blocking_the_console()
    {
        var (model, _, process) = MakeChecks();

        Run(model, "checks run fixed");

        Assert.Equal("Running fixed...", model.Lines.Last().Text);
        WaitFor(model, "failing: Registry.Foo");
        Assert.Contains(new ConsoleLine(ConsoleSeverity.Error, "failed Registry.Foo (1.25s)"), model.Lines);
        Assert.Contains(new ConsoleLine(ConsoleSeverity.Info, "passed Registry.Bar (0.5s)"), model.Lines);
        Assert.Contains(new ConsoleLine(ConsoleSeverity.Error, "FAIL Fixed abcdef0 1/2 skip=0 3s"), model.Lines);
        Assert.Contains(new ConsoleLine(ConsoleSeverity.Info, "Result: C:\\ci\\results\\fixed.json"), model.Lines);
        Assert.Contains(new ConsoleLine(ConsoleSeverity.Error, "Fixed: FAIL 3/4 skip 1 12.5s"), model.Lines);
        Assert.Equal(new[] { "-Box", "Fixed", "-Tier", "fast", "-Json" }, process.Snapshot().First(c => c[0] == "-Box"));
    }

    [Fact]
    public void Checks_errors_from_validation_arrive_as_error_lines_and_other_commands_still_reach_the_wrapped_host()
    {
        var (model, inner, process) = MakeChecks();

        Run(model, "echo hi");
        Run(model, "checks run");
        Run(model, "checks bogus");
        Run(model, "checks changed --x");
        Run(model, "checks cancel");
        Run(model, "checks panel");

        Assert.Equal("echo hi", inner.Executed.Single().Line);
        Assert.Equal(4, model.Lines.Count(l => l.Severity == ConsoleSeverity.Error));
        Assert.Contains(model.Lines, l => l.Text == "Nothing is running.");
        Assert.Contains(model.Lines, l => l.Text == ChecksConsoleHost.PanelUnavailable);
        Assert.Empty(process.Snapshot());

        Run(model, "checks run nope");
        WaitFor(model, "Unknown box: nope");
        Assert.Contains(new ConsoleLine(ConsoleSeverity.Error, "Unknown box: nope"), model.Lines);
        Assert.DoesNotContain(process.Snapshot(), c => c[0] == "-Box");
    }

    [Fact]
    public void Help_lists_the_checks_command_next_to_the_wrapped_hosts_commands()
    {
        var (model, _, _) = MakeChecks();

        Run(model, "help");

        Assert.Contains("checks", model.Lines.Last().Text);
        Assert.Contains("echo", model.Lines.Last().Text);
    }
}
