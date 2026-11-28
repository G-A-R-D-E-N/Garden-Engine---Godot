using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OpenCommonwealth.Services.Ui.DevConsole.DebugChecks;

public sealed class ChecksConsoleHost : IConsoleHost
{
    public const string Command = "checks";
    public const string Usage = "Usage: checks | checks run <box> [tier] [test] | checks all | checks changed [base] | checks results | checks cancel | checks panel";
    public const string PanelUnavailable = "The checks panel cannot be opened from here.";

    private readonly IConsoleHost _inner;
    private readonly RdfClient _client;
    private readonly Action<ConsoleSeverity, string> _emit;
    private readonly Func<bool>? _openPanel;

    public ChecksConsoleHost(IConsoleHost inner, RdfClient client, Action<ConsoleSeverity, string> emit, Func<bool>? openPanel = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(emit);
        _inner = inner;
        _client = client;
        _emit = emit;
        _openPanel = openPanel;
    }

    public ConsoleResult Execute(string line, ConsoleTarget? target)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !parts[0].Equals(Command, StringComparison.OrdinalIgnoreCase))
            return _inner.Execute(line, target);
        var verb = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;
        switch (verb)
        {
            case "":
                return Start("Listing checks...", ListChecks);
            case "run" when parts.Length >= 3:
                var box = parts[2];
                var tier = parts.Length > 3 ? parts[3] : null;
                var test = parts.Length > 4 ? string.Join(' ', parts[4..]) : null;
                return Start("Running " + box + "...", () => RunBox(box, tier, test));
            case "all" when parts.Length == 2:
                return Start("Running all checks...", () => RunScope(_client.RunAllAsync(OnEvent)));
            case "changed" when parts.Length <= 3:
                var baseRef = parts.Length > 2 ? parts[2] : null;
                if (baseRef is not null && !RdfClient.IsSafeRef(baseRef)) return ConsoleResult.Error("Not a valid base ref: " + baseRef);
                return Start("Running changed checks...", () => RunScope(_client.RunChangedAsync(baseRef, OnEvent)));
            case "results" when parts.Length == 2:
                return Start("Reading results...", ShowResults);
            case "cancel" when parts.Length == 2:
                if (!_client.IsBusy) return ConsoleResult.Info("Nothing is running.");
                _client.Cancel();
                return ConsoleResult.Info("Cancel requested.");
            case "panel" when parts.Length == 2:
                return _openPanel is not null && _openPanel() ? ConsoleResult.Info("Opened the checks panel.") : ConsoleResult.Error(PanelUnavailable);
            default:
                return ConsoleResult.Error(Usage);
        }
    }

    public IReadOnlyList<string> Complete(string prefix)
    {
        var matches = _inner.Complete(prefix).ToList();
        if (Command.StartsWith(prefix ?? string.Empty, StringComparison.OrdinalIgnoreCase) && !matches.Contains(Command))
            matches.Add(Command);
        return matches;
    }

    public ConsoleTarget? Pick(double x, double y) => _inner.Pick(x, y);

    private ConsoleResult Start(string started, Func<Task> work)
    {
        _ = Report(work);
        return ConsoleResult.Info(started);
    }

    private async Task Report(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _emit(ConsoleSeverity.Warning, "Cancelled. The box may show a stale RUNNING state until its next run.");
        }
        catch (Exception ex)
        {
            _emit(ConsoleSeverity.Error, ex.Message);
        }
    }

    private async Task ListChecks()
    {
        var list = await _client.ListAsync().ConfigureAwait(false);
        foreach (var box in list.Boxes)
            _emit(Severity(box.Last), box.Id + " [" + string.Join(",", box.Tiers) + "] " + box.Last.Text + " - " + box.Title);
        _emit(ConsoleSeverity.Info, list.Boxes.Count + " boxes. Results: " + list.ResultsRoot);
    }

    private async Task RunBox(string box, string? tier, string? test)
    {
        await _client.RunBoxAsync(box, tier, test, OnEvent).ConfigureAwait(false);
        await Finish(box).ConfigureAwait(false);
    }

    private async Task RunScope(Task<RdfEvent?> run)
    {
        await run.ConfigureAwait(false);
        await Finish(null).ConfigureAwait(false);
    }

    private async Task Finish(string? box)
    {
        var list = await _client.ListAsync().ConfigureAwait(false);
        foreach (var entry in list.Boxes.Where(b => box is null ? b.Last.Status == "FAIL" : string.Equals(b.Id, box, StringComparison.OrdinalIgnoreCase)))
        {
            _emit(Severity(entry.Last), entry.Id + ": " + entry.Last.Text);
            foreach (var failing in entry.Last.Failing)
                _emit(ConsoleSeverity.Error, "  failing: " + failing);
        }
    }

    private async Task ShowResults()
    {
        var results = await _client.ResultsAsync().ConfigureAwait(false);
        foreach (var box in results.Boxes)
            _emit(Severity(box.Last), box.Id + " " + box.Last.Text);
        _emit(ConsoleSeverity.Info, results.Boxes.Count + " results. Folder: " + results.ResultsRoot);
    }

    private void OnEvent(RdfEvent evt)
    {
        _emit(evt.IsFailure ? ConsoleSeverity.Error : ConsoleSeverity.Info, evt.Describe());
        if (evt.Kind == RdfEvent.Done && evt.ResultPath is { Length: > 0 } path)
            _emit(ConsoleSeverity.Info, "Result: " + path);
    }

    private static ConsoleSeverity Severity(RdfLast last) =>
        last.Status == "FAIL" ? ConsoleSeverity.Error : last.Status == "PASS" ? ConsoleSeverity.Info : ConsoleSeverity.Warning;
}
