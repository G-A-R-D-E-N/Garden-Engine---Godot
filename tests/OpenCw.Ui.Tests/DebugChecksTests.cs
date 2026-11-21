using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCommonwealth.Services.Ui.DevConsole.DebugChecks;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

sealed class FakeRdfProcess : IRdfProcess
{
    public const string ListJson = """
        {"version":1,"results_root":"C:\\ci\\results","boxes":[
          {"id":"Fixed","title":"Fixed box","kind":"engine","tags":["papyrus"],"tiers":["fast","slow"],
           "last":{"status":"FAIL","sha":"abc1234","passed":3,"total":4,"skip":1,"seconds":12.5,"at":"2026-11-19T10:00:00Z","failing":["Registry.Foo"]}},
          {"id":"bare","title":"Bare","kind":"radicle","tags":[],"tiers":["fast"]}]}
        """;

    public static readonly string[] RunLines =
    {
        """{"event":"start","box":"Fixed","tier":"fast"}""",
        "plain log text from the launcher",
        """{"event":"phase","box":"Fixed","name":"test","total":2}""",
        """{"event":"test","box":"Fixed","name":"Registry.Foo","status":"failed","seconds":1.25}""",
        """{"event":"test","box":"Fixed","name":"Registry.Bar","status":"passed","seconds":0.5}""",
        """{"event":"surprise","x":1}""",
        """{"event":"done","box":"Fixed","status":"FAIL","exit_code":1,"result_path":"C:\\ci\\results\\fixed.json","line":"FAIL Fixed abcdef0 1/2 skip=0 3s"}""",
    };

    public List<string[]> Calls { get; } = new();

    public Func<string[], (int Code, string[] Out, string[] Err)> Respond { get; set; } = args =>
    {
        if (args[0] == "-List") return (0, new[] { ListJson }, Array.Empty<string>());
        if (args[0] == "-Tests") return (0, new[] { """{"tests":["Registry.Foo","Registry.Bar"]}""" }, Array.Empty<string>());
        return (1, RunLines, Array.Empty<string>());
    };

    public Func<Task>? Gate { get; set; }

    public async Task<int> RunAsync(IReadOnlyList<string> args, Action<string> onStdout, Action<string> onStderr, CancellationToken cancel)
    {
        var copy = args.ToArray();
        lock (Calls) Calls.Add(copy);
        if (Gate is not null && copy[0] != "-List") await Gate().WaitAsync(cancel);
        var (code, output, errors) = Respond(copy);
        foreach (var line in output) onStdout(line);
        foreach (var line in errors) onStderr(line);
        return code;
    }

    public string[][] Snapshot()
    {
        lock (Calls) return Calls.ToArray();
    }
}

public class DebugChecksTests
{
    [Fact]
    public void List_parse_keeps_the_last_result_and_gives_a_box_without_one_the_NONE_state()
    {
        var list = RdfJson.ParseList(FakeRdfProcess.ListJson);

        Assert.Equal("C:\\ci\\results", list.ResultsRoot);
        var fixedBox = list.Boxes[0];
        Assert.Equal(new[] { "fast", "slow" }, fixedBox.Tiers);
        Assert.Equal("FAIL", fixedBox.Last.Status);
        Assert.Equal(new[] { "Registry.Foo" }, fixedBox.Last.Failing);
        Assert.Equal("FAIL 3/4 skip 1 12.5s", fixedBox.Last.Text);
        Assert.Equal("NONE", list.Boxes[1].Last.Status);
        Assert.Empty(list.Boxes[1].Last.Failing);
    }

    [Fact]
    public void List_parse_refuses_a_non_json_document_instead_of_showing_an_empty_list()
    {
        Assert.Throws<RdfException>(() => RdfJson.ParseList("Cannot find path 'x'."));
        Assert.Throws<RdfException>(() => RdfJson.ParseList("[1,2]"));
    }

    [Fact]
    public void Tests_parse_reads_names_and_the_unavailable_reason()
    {
        var names = RdfJson.ParseTests("""{"tests":["A.B","C.D"]}""");
        var none = RdfJson.ParseTests("""{"unavailable":"Catch2 binary not built"}""");
        var notBuilt = RdfJson.ParseTests("""{"version":1,"box":"G16","tier":"fast","available":false,"reason":"tests unavailable until the first run has built the box","count":0,"tests":[]}""");

        Assert.Equal(new[] { "A.B", "C.D" }, names.Tests);
        Assert.Null(names.Unavailable);
        Assert.Empty(none.Tests);
        Assert.Equal("Catch2 binary not built", none.Unavailable);
        Assert.Equal("tests unavailable until the first run has built the box", notBuilt.Unavailable);
    }

    [Fact]
    public void Event_parse_skips_log_noise_malformed_json_and_unknown_events()
    {
        Assert.Null(RdfJson.ParseEvent("plain log text"));
        Assert.Null(RdfJson.ParseEvent("{\"event\":\"test\",\"name\":"));
        Assert.Null(RdfJson.ParseEvent("""{"event":"surprise"}"""));
        Assert.Null(RdfJson.ParseEvent("""{"event":"test","status":"PASS"}"""));
        Assert.Null(RdfJson.ParseEvent(""));

        var test = RdfJson.ParseEvent("""{"event":"test","box":"G16","name":"A.B","status":"failed","seconds":2.5}""")!;
        Assert.Equal(("G16", "A.B", "failed", 2.5, true), (test.Box, test.Name, test.Status, test.Seconds, test.IsFailure));
        Assert.Equal("failed A.B (2.5s)", test.Describe());
        var passed = RdfJson.ParseEvent("""{"event":"test","name":"A.C","status":"passed","seconds":0.1}""")!;
        var skipped = RdfJson.ParseEvent("""{"event":"test","name":"A.D","status":"skipped","seconds":0}""")!;
        Assert.False(passed.IsFailure);
        Assert.False(skipped.IsFailure);
        Assert.True(skipped.IsSkip);
    }

    [Fact]
    public void Results_parse_reads_the_latest_index_document()
    {
        var list = RdfJson.ParseResults("""{"version":1,"results_root":"R","results":[{"id":"G16","last":{"status":"FAIL","sha":"abcdef0","passed":1,"total":2,"skip":0,"seconds":19,"at":"t","failing":["X.Y"],"error":"build-failed"},"running":null}]}""");

        Assert.Equal("R", list.ResultsRoot);
        Assert.Equal("FAIL 1/2 19s (build-failed)", list.Boxes.Single().Last.Text);
        Assert.Equal(new[] { "X.Y" }, list.Boxes.Single().Last.Failing);
    }

    [Fact]
    public async Task A_changed_run_reports_the_plan_and_every_boxs_done_line_and_returns_the_last()
    {
        var process = new FakeRdfProcess
        {
            Respond = args => (1, new[]
            {
                """{"event":"plan","boxes":["G15","G16"],"unknown":false}""",
                """{"event":"done","box":"G15","status":"PASS","line":"PASS G15 abcdef0 8/8 skip=0 20s"}""",
                """{"event":"done","box":"G16","status":"FAIL","line":"FAIL G16 abcdef0 1/2 skip=0 9s"}""",
            }, Array.Empty<string>()),
        };
        var client = new RdfClient(process);
        var seen = new List<RdfEvent>();

        var last = await client.RunChangedAsync("origin/main", seen.Add);

        Assert.Equal(new[] { "-Changed", "-Base", "origin/main", "-Json" }, process.Snapshot().Single());
        Assert.Equal("Plan: G15, G16", seen[0].Describe());
        Assert.Equal(new[] { false, true }, seen.Where(e => e.Kind == RdfEvent.Done).Select(e => e.IsFailure));
        Assert.Equal("G16", last!.Box);
    }

    [Fact]
    public async Task A_failing_run_streams_its_events_and_returns_the_done_line_despite_the_nonzero_exit()
    {
        var process = new FakeRdfProcess();
        var client = new RdfClient(process);
        var seen = new List<RdfEvent>();

        var done = await client.RunBoxAsync("fixed", null, null, seen.Add);

        Assert.Equal("FAIL Fixed abcdef0 1/2 skip=0 3s", done!.Line);
        Assert.True(done.IsFailure);
        Assert.Equal("C:\\ci\\results\\fixed.json", done.ResultPath);
        Assert.Equal(new[] { "start", "phase", "test", "test", "done" }, seen.Select(e => e.Kind));
        Assert.Equal(new[] { true, false }, seen.Where(e => e.Kind == RdfEvent.Test).Select(e => e.IsFailure));
        Assert.Equal(new[] { "-Box", "Fixed", "-Tier", "fast", "-Json" }, process.Snapshot().Last());
        Assert.False(client.IsBusy);
    }

    [Fact]
    public async Task A_run_for_one_test_passes_the_canonical_box_tier_and_test_as_separate_arguments()
    {
        var process = new FakeRdfProcess();
        var client = new RdfClient(process);

        await client.RunBoxAsync("FIXED", "SLOW", "Registry.Bar", _ => { });

        Assert.Equal(new[] { "-Box", "Fixed", "-Tier", "slow", "-Test", "Registry.Bar", "-Json" }, process.Snapshot().Last());
    }

    [Theory]
    [InlineData("nope", null, null, "Unknown box")]
    [InlineData("-All", null, null, "Not a valid box id")]
    [InlineData("bare", "slow", null, "Unknown tier")]
    [InlineData("Fixed", "fast", "Registry.Missing", "Unknown test")]
    [InlineData("Fixed", "fast", "-Json", "Unknown test")]
    public async Task Box_tier_and_test_values_the_launcher_did_not_list_never_reach_a_run(string box, string? tier, string? test, string message)
    {
        var process = new FakeRdfProcess();
        var client = new RdfClient(process);

        var error = await Assert.ThrowsAsync<RdfException>(() => client.RunBoxAsync(box, tier, test, _ => { }));

        Assert.Contains(message, error.Message);
        Assert.DoesNotContain(process.Snapshot(), call => call.Contains("-Json") && call[0] != "-List" && call[0] != "-Tests");
        Assert.False(client.IsBusy);
    }

    [Fact]
    public async Task A_single_test_is_refused_with_the_launchers_reason_when_the_box_cannot_run_one()
    {
        var process = new FakeRdfProcess();
        var inner = process.Respond;
        process.Respond = args => args[0] == "-Tests" ? (0, new[] { """{"unavailable":"no per-test filter for this suite"}""" }, Array.Empty<string>()) : inner(args);
        var client = new RdfClient(process);

        var error = await Assert.ThrowsAsync<RdfException>(() => client.RunBoxAsync("Fixed", "fast", "Registry.Foo", _ => { }));

        Assert.Contains("no per-test filter for this suite", error.Message);
        Assert.DoesNotContain(process.Snapshot(), call => call[0] == "-Box");
    }

    [Theory]
    [InlineData("origin/main", true)]
    [InlineData("HEAD~3", true)]
    [InlineData("nomad/dev", true)]
    [InlineData("-All", false)]
    [InlineData("a..b", false)]
    [InlineData("main; calc", false)]
    [InlineData("main$(x)", false)]
    [InlineData("", false)]
    public void A_base_ref_must_be_a_plain_ref_that_cannot_bind_as_a_launcher_parameter(string value, bool safe)
    {
        Assert.Equal(safe, RdfClient.IsSafeRef(value));
    }

    [Fact]
    public async Task Changed_with_an_unsafe_base_never_starts_the_launcher()
    {
        var process = new FakeRdfProcess();
        var client = new RdfClient(process);

        await Assert.ThrowsAsync<RdfException>(() => client.RunChangedAsync("-All", _ => { }));

        Assert.Empty(process.Snapshot());
    }

    [Fact]
    public async Task A_second_call_while_one_runs_is_refused_and_cancel_ends_the_first_without_leaving_the_client_busy()
    {
        var process = new FakeRdfProcess { Gate = () => new TaskCompletionSource().Task };
        var client = new RdfClient(process);
        var first = client.RunAllAsync(_ => { });
        while (process.Snapshot().Length == 0) await Task.Delay(5);

        Assert.True(client.IsBusy);
        await Assert.ThrowsAsync<RdfBusyException>(() => client.ListAsync());
        client.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        Assert.False(client.IsBusy);
        Assert.Equal("fixed", (await client.ListAsync()).Boxes[0].Id.ToLowerInvariant());
    }

    [Fact]
    public async Task A_changed_run_that_selects_no_boxes_returns_a_no_checks_affected_result()
    {
        var process = new FakeRdfProcess { Respond = _ => (0, new[] { """{"event":"plan","boxes":[],"unknown":false}""" }, Array.Empty<string>()) };
        var client = new RdfClient(process);
        var seen = new List<RdfEvent>();

        var done = await client.RunChangedAsync(null, seen.Add);

        Assert.Equal(RdfEvent.NoChecksAffected, done!.Line);
        Assert.False(done.IsFailure);
        Assert.Equal(new[] { "plan", "done" }, seen.Select(e => e.Kind));

        process.Respond = _ => (0, new[] { """{"event":"plan","boxes":["G15"],"unknown":false}""" }, Array.Empty<string>());
        await Assert.ThrowsAsync<RdfException>(() => client.RunChangedAsync(null, _ => { }));

        process.Respond = _ => (0, new[] { """{"event":"plan","unknown":true}""" }, Array.Empty<string>());
        await Assert.ThrowsAsync<RdfException>(() => client.RunChangedAsync(null, _ => { }));
    }

    [Fact]
    public async Task Only_the_last_stderr_lines_of_a_noisy_failing_run_reach_the_error_text()
    {
        var noise = Enumerable.Range(1, 500).Select(i => "log " + i).ToArray();
        var process = new FakeRdfProcess { Respond = _ => (2, Array.Empty<string>(), noise) };
        var client = new RdfClient(process);

        var error = await Assert.ThrowsAsync<RdfException>(() => client.RunAllAsync(_ => { }));

        Assert.Contains("log 500", error.Message);
        Assert.DoesNotContain("log 400", error.Message);
    }

    static void Pump(DebugChecksController panel, Func<bool> until)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < limit)
        {
            panel.Tick(0.01);
            if (until()) return;
            Thread.Sleep(5);
        }
        Assert.True(until());
    }

    [Fact]
    public void The_panel_cycles_tiers_refuses_a_second_run_and_refreshes_the_list_after_a_cancel()
    {
        var process = new FakeRdfProcess { Gate = () => new TaskCompletionSource().Task };
        var panel = new DebugChecksController(new RdfClient(process));
        Pump(panel, () => panel.Rows.RowCount > 4 && !panel.IsRunning);

        Assert.Equal("fast", panel.Tier);
        panel.Handle(MenuInputAction.Right);
        Assert.Equal("slow", panel.Tier);
        panel.Handle(MenuInputAction.Right);
        Assert.Equal("fast", panel.Tier);
        panel.Handle(MenuInputAction.Left);
        Assert.Equal("slow", panel.Tier);

        panel.RunAll();
        panel.RunChanged();
        Pump(panel, () => process.Snapshot().Any(c => c[0] == "-All"));
        Assert.Contains("already in progress", panel.Message);
        Assert.DoesNotContain(process.Snapshot(), c => c[0] == "-Changed");
        Assert.True(panel.IsRunning);

        panel.Cancel();
        Pump(panel, () => !panel.IsRunning && panel.Status.Contains("stale RUNNING"));

        Assert.Equal(2, process.Snapshot().Count(c => c[0] == "-List"));
    }

    [Fact]
    public async Task A_run_that_ends_without_a_done_event_reports_the_launchers_error_text()
    {
        var process = new FakeRdfProcess { Respond = _ => (2, new[] { "garbage" }, new[] { "rdf: git fetch failed" }) };
        var client = new RdfClient(process);

        var error = await Assert.ThrowsAsync<RdfException>(() => client.RunAllAsync(_ => { }));

        Assert.Contains("exit 2", error.Message);
        Assert.Contains("rdf: git fetch failed", error.Message);
    }

    [Fact]
    public async Task A_nonzero_exit_after_a_pass_report_is_an_error_not_a_pass()
    {
        var done = """{"event":"done","box":"G15","status":"PASS","line":"PASS G15 abcdef0 8/8 skip=0 20s"}""";
        var process = new FakeRdfProcess();
        var original = process.Respond;
        process.Respond = args => args[0] == "-List" ? original(args) : (3, new[] { done }, new[] { "rdf: cleanup failed" });
        var client = new RdfClient(process);

        var error = await Assert.ThrowsAsync<RdfException>(() => client.RunBoxAsync("fixed", null, null, _ => { }));

        Assert.Contains("code 3", error.Message);
        Assert.Contains("not a pass", error.Message);
        Assert.Contains("rdf: cleanup failed", error.Message);

        process.Respond = args => args[0] == "-List" ? original(args) : (0, new[] { done }, Array.Empty<string>());
        Assert.False((await client.RunBoxAsync("fixed", null, null, _ => { }))!.IsFailure);
    }

    [Theory]
    [InlineData("""{"event":"plan","boxes":"G15"}""")]
    [InlineData("""{"event":"plan","boxes":[1,2]}""")]
    [InlineData("""{"event":"plan","boxes":{"a":1}}""")]
    [InlineData("""{"event":"plan","boxes":[""]}""")]
    public async Task A_malformed_plan_is_dropped_and_never_becomes_a_no_checks_affected_pass(string plan)
    {
        Assert.Null(RdfJson.ParseEvent(plan));
        var process = new FakeRdfProcess { Respond = _ => (0, new[] { plan }, Array.Empty<string>()) };
        var client = new RdfClient(process);

        await Assert.ThrowsAsync<RdfException>(() => client.RunChangedAsync(null, _ => { }));
    }

    [Fact]
    public void List_results_and_tests_parse_refuse_documents_that_lack_their_arrays_or_ids()
    {
        Assert.Throws<RdfException>(() => RdfJson.ParseList("{}"));
        Assert.Throws<RdfException>(() => RdfJson.ParseList("""{"boxes":"x"}"""));
        Assert.Throws<RdfException>(() => RdfJson.ParseList("""{"boxes":[{"title":"no id"}]}"""));
        Assert.Throws<RdfException>(() => RdfJson.ParseList("""{"boxes":["text"]}"""));
        Assert.Throws<RdfException>(() => RdfJson.ParseList("""{"boxes":[{"id":"A","last":"FAIL"}]}"""));
        Assert.Empty(RdfJson.ParseList("""{"boxes":[]}""").Boxes);

        Assert.Throws<RdfException>(() => RdfJson.ParseResults("{}"));
        Assert.Throws<RdfException>(() => RdfJson.ParseResults("""{"results":[{"last":{"status":"PASS"}}]}"""));
        Assert.Empty(RdfJson.ParseResults("""{"results":[]}""").Boxes);

        Assert.Throws<RdfException>(() => RdfJson.ParseTests("{}"));
        Assert.Throws<RdfException>(() => RdfJson.ParseTests("""{"tests":"A.B"}"""));
        Assert.Throws<RdfException>(() => RdfJson.ParseTests("""{"tests":["A.B",3]}"""));
        Assert.Empty(RdfJson.ParseTests("""{"tests":[]}""").Tests);
    }

    static void SelectBox(DebugChecksController panel, string box)
    {
        for (var i = 0; i < 20 && panel.SelectedBox?.Id != box; i++) panel.Handle(MenuInputAction.Down);
        Assert.Equal(box, panel.SelectedBox?.Id);
    }

    [Fact]
    public void A_box_without_the_displayed_tier_is_refused_and_no_run_starts()
    {
        var process = new FakeRdfProcess();
        var panel = new DebugChecksController(new RdfClient(process));
        Pump(panel, () => panel.Rows.RowCount > 4 && !panel.IsRunning);
        panel.Handle(MenuInputAction.Right);
        Assert.Equal("slow", panel.Tier);

        panel.RunBox("bare");

        Assert.Contains("no slow tier", panel.Message);
        Assert.False(panel.IsRunning);
        Assert.DoesNotContain(process.Snapshot(), c => c[0] == "-Box");

        SelectBox(panel, "bare");
        panel.Handle(MenuInputAction.TabNext);
        Assert.Contains("no slow tier", panel.Message);
        Assert.DoesNotContain(process.Snapshot(), c => c[0] == "-Tests");
    }

    [Fact]
    public void The_chosen_tier_survives_a_list_refresh_by_name_and_falls_back_to_fast_when_it_disappears()
    {
        var process = new FakeRdfProcess { Gate = () => new TaskCompletionSource().Task };
        var reordered = """{"version":1,"results_root":"R","boxes":[{"id":"Fixed","title":"Fixed","kind":"engine","tags":[],"tiers":["slow","fast"]}]}""";
        var fastOnly = """{"version":1,"results_root":"R","boxes":[{"id":"Fixed","title":"Fixed","kind":"engine","tags":[],"tiers":["fast"]}]}""";
        var lists = 0;
        var original = process.Respond;
        process.Respond = args => args[0] == "-List" ? (0, new[] { ++lists == 1 ? FakeRdfProcess.ListJson : lists == 2 ? reordered : fastOnly }, Array.Empty<string>()) : original(args);
        var panel = new DebugChecksController(new RdfClient(process));
        Pump(panel, () => panel.Rows.RowCount > 4 && !panel.IsRunning);
        panel.Handle(MenuInputAction.Right);
        Assert.Equal("slow", panel.Tier);

        panel.RunAll();
        Pump(panel, () => process.Snapshot().Any(c => c[0] == "-All"));
        panel.Cancel();
        Pump(panel, () => !panel.IsRunning && lists >= 2 && panel.Status.Contains("stale RUNNING"));
        Assert.Equal("slow", panel.Tier);

        panel.RunAll();
        Pump(panel, () => process.Snapshot().Count(c => c[0] == "-All") == 2);
        panel.Cancel();
        Pump(panel, () => !panel.IsRunning && lists >= 3 && panel.Status.Contains("stale RUNNING"));
        Assert.Equal("fast", panel.Tier);
    }

    [Fact]
    public void Test_names_are_cached_per_tier_so_a_tier_change_loads_that_tiers_tests()
    {
        var process = new FakeRdfProcess();
        var original = process.Respond;
        process.Respond = args => args[0] == "-Tests"
            ? (0, new[] { args.Contains("slow") ? """{"tests":["Slow.One"]}""" : """{"tests":["Fast.One","Fast.Two"]}""" }, Array.Empty<string>())
            : original(args);
        var panel = new DebugChecksController(new RdfClient(process));
        Pump(panel, () => panel.Rows.RowCount > 4 && !panel.IsRunning);
        SelectBox(panel, "Fixed");

        panel.Handle(MenuInputAction.TabNext);
        Pump(panel, () => !panel.IsRunning && panel.Status == "2 tests.");
        panel.Handle(MenuInputAction.Right);
        panel.Handle(MenuInputAction.TabNext);
        Pump(panel, () => !panel.IsRunning && panel.Status == "1 tests.");

        var calls = process.Snapshot().Where(c => c[0] == "-Tests").ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Contains("slow", calls[1]);
        Assert.Contains(panel.DetailLines, l => l.Text.Contains("Slow.One"));
        Assert.DoesNotContain(panel.DetailLines, l => l.Text.Contains("Fast.One"));
    }

    [Fact]
    public void A_late_test_list_for_a_tier_that_is_no_longer_shown_is_cached_without_taking_the_focus()
    {
        var release = new TaskCompletionSource();
        var process = new FakeRdfProcess();
        var original = process.Respond;
        process.Respond = args => args[0] == "-Tests" ? (0, new[] { """{"tests":["Fast.One"]}""" }, Array.Empty<string>()) : original(args);
        var panel = new DebugChecksController(new RdfClient(process));
        Pump(panel, () => panel.Rows.RowCount > 4 && !panel.IsRunning);
        SelectBox(panel, "Fixed");
        process.Gate = () => release.Task;

        panel.Handle(MenuInputAction.TabNext);
        panel.Handle(MenuInputAction.Right);
        release.SetResult();
        Pump(panel, () => !panel.IsRunning);

        Assert.Equal("slow", panel.Tier);
        Assert.NotEqual("1 tests.", panel.Status);
        Assert.DoesNotContain(panel.DetailLines, l => l.Text.Contains("Fast.One"));
    }

    [Fact]
    public async Task A_missing_launcher_script_is_a_clear_error_that_names_the_environment_variable()
    {
        var process = new RdfProcess(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid().ToString("N"), "rdf.ps1"));

        var error = await Assert.ThrowsAsync<RdfException>(() => process.RunAsync(new[] { "-List" }, _ => { }, _ => { }, CancellationToken.None));

        Assert.Contains(RdfClient.ScriptVariable, error.Message);
    }
}
