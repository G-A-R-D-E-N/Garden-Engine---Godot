using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.DevConsole.DebugChecks;

public sealed record CheckRow(string Id, string Text, string? Box);

public sealed class DebugChecksController : ListDetailController<CheckRow>
{
    public const string RunAllId = "act:all";
    public const string RunChangedId = "act:changed";
    public const string CancelId = "act:cancel";
    public const string RefreshId = "act:refresh";
    public const int MaxTestLines = 24;
    public const int MaxFailingLines = 20;
    public const int MaxLiveLines = 12;

    private static readonly IReadOnlyList<MenuHint> PanelHints = new[]
    {
        new MenuHint(MenuInputAction.Accept, "Run"),
        new MenuHint(MenuInputAction.Left, "Tier"),
        new MenuHint(MenuInputAction.TabNext, "Test"),
        new MenuHint(MenuInputAction.Back, "Close"),
    };

    private readonly RdfClient _client;
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly List<string> _tiers = new();
    private readonly Dictionary<string, List<string>> _testNames = new(StringComparer.Ordinal);
    private readonly List<RdfEvent> _live = new();
    private RdfList? _list;
    private int _tierIndex;
    private int _focus = -1;
    private bool _tiersChosen;
    private bool _loadPending;
    private string? _focusBox;
    private string? _runLabel;
    private string? _liveBox;
    private string _phase = string.Empty;
    private string _status = "Loading the check list...";

    public DebugChecksController(RdfClient client)
        : base(new PlainListModel<CheckRow>(row => row.Id, row => row.Text))
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        Rows.Accepted += OnAccepted;
        Rows.Changed += OnRowsChanged;
        Rows.SetRows(ActionRows());
        if (_client.Boxes is { } known) ApplyList(known);
        if (_list is null && _client.IsBusy)
        {
            _loadPending = true;
            _status = "A check run is in progress; the list loads when it ends.";
        }
        else
        {
            Begin("Loading", Refresh);
        }
    }

    public override string Title => "Debug checks";

    public override string Header => _list is null ? "Real-data checks" : "Real-data checks - tier " + (Tier ?? "?");

    public override IReadOnlyList<MenuHint> Hints => PanelHints;

    public string? Tier => _tiers.Count == 0 ? null : _tiers[_tierIndex];

    public bool IsRunning => _runLabel is not null;

    public string Status => _status;

    public RdfBox? SelectedBox => Rows.Selected is { Box: { } id } ? _list?.Boxes.FirstOrDefault(b => b.Id == id) : null;

    public override IReadOnlyList<MenuTextLine> DetailLines => BuildLines();

    public override void Tick(double deltaSeconds)
    {
        base.Tick(deltaSeconds);
        var any = false;
        if (_loadPending && _runLabel is null && !_client.IsBusy)
        {
            if (Begin("Loading", Refresh))
            {
                _loadPending = false;
                _status = "Loading the check list...";
            }
            any = true;
        }
        while (_posted.TryDequeue(out var action))
        {
            action();
            any = true;
        }
        if (any) Raise();
    }

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        switch (action)
        {
            case MenuInputAction.Left:
                return StepTier(-1);
            case MenuInputAction.Right:
                return StepTier(1);
            case MenuInputAction.TabNext:
                return StepTest(1);
            case MenuInputAction.TabPrev:
                return StepTest(-1);
            case MenuInputAction.Back:
                Finish(false);
                return true;
            default:
                return Rows.Handle(action);
        }
    }

    public void RunAll() => Begin("all checks", () => Scope(_client.RunAllAsync(Live(null))));

    public void RunChanged() => Begin("changed checks", () => Scope(_client.RunChangedAsync(null, Live(null))));

    public void Cancel()
    {
        if (!IsRunning) return;
        _client.Cancel();
        Say("Cancel requested.");
    }

    public void RunBox(string box, string? test = null)
    {
        var tier = _list?.Boxes.FirstOrDefault(b => b.Id == box) is { } known && Tier is { } t && known.Tiers.Contains(t) ? t : null;
        Begin(test is null ? box : box + " " + test, async () =>
        {
            await _client.RunBoxAsync(box, tier, test, Live(box)).ConfigureAwait(false);
            await Refresh().ConfigureAwait(false);
        });
    }

    private static IEnumerable<CheckRow> ActionRows() => new[]
    {
        new CheckRow(RunAllId, "Run all checks", null),
        new CheckRow(RunChangedId, "Run changed checks", null),
        new CheckRow(CancelId, "Cancel the running check", null),
        new CheckRow(RefreshId, "Refresh the list", null),
    };

    private void OnAccepted(CheckRow row)
    {
        switch (row.Id)
        {
            case RunAllId:
                RunAll();
                break;
            case RunChangedId:
                RunChanged();
                break;
            case CancelId:
                Cancel();
                break;
            case RefreshId:
                Begin("Loading", Refresh);
                break;
            default:
                if (row.Box is { } box)
                    RunBox(box, FocusedTest(box));
                break;
        }
    }

    private void OnRowsChanged()
    {
        var box = Rows.Selected?.Box;
        if (box == _focusBox) return;
        _focusBox = box;
        _focus = -1;
        Raise();
    }

    private string? FocusedTest(string box) =>
        _testNames.TryGetValue(box, out var names) && _focus >= 0 && _focus < names.Count ? names[_focus] : null;

    private bool StepTier(int delta)
    {
        if (_tiers.Count == 0) return true;
        _tierIndex = ((_tierIndex + delta) % _tiers.Count + _tiers.Count) % _tiers.Count;
        _focus = -1;
        _testNames.Clear();
        Raise();
        return true;
    }

    private bool StepTest(int delta)
    {
        if (SelectedBox is not { } box) return true;
        if (!_testNames.TryGetValue(box.Id, out var names))
        {
            var tier = box.Tiers.Contains(Tier ?? string.Empty) ? Tier : null;
            Begin("Loading tests of " + box.Id, async () =>
            {
                var tests = await _client.TestsAsync(box.Id, tier).ConfigureAwait(false);
                Post(() =>
                {
                    _testNames[box.Id] = tests.Tests.ToList();
                    _status = tests.Unavailable is { } reason ? "Single tests are unavailable: " + reason : tests.Tests.Count + " tests.";
                    if (tests.Tests.Count > 0) _focus = 0;
                });
            });
            return true;
        }
        if (names.Count == 0) return true;
        _focus = _focus < 0 ? (delta > 0 ? 0 : names.Count - 1) : _focus + delta;
        if (_focus >= names.Count || _focus < 0) _focus = -1;
        Raise();
        return true;
    }

    private Action<RdfEvent> Live(string? box)
    {
        Post(() => _liveBox = box);
        return evt => Post(() => Apply(evt, box));
    }

    private void Apply(RdfEvent evt, string? box)
    {
        switch (evt.Kind)
        {
            case RdfEvent.Phase:
                _phase = evt.Name ?? string.Empty;
                break;
            case RdfEvent.Test:
                var index = _live.FindIndex(e => e.Name == evt.Name && e.Box == evt.Box);
                if (index >= 0) _live[index] = evt;
                else _live.Add(evt);
                if (box is not null && evt.Name is { } name)
                {
                    if (!_testNames.TryGetValue(box, out var names)) _testNames[box] = names = new List<string>();
                    if (!names.Contains(name)) names.Add(name);
                }
                break;
            case RdfEvent.Plan:
                _status = evt.Line ?? "Plan.";
                break;
            case RdfEvent.Done:
                _status = evt.Line ?? "Done.";
                if (evt.ResultPath is { Length: > 0 } path) _status += " Result: " + path;
                break;
        }
    }

    private async Task Scope(Task<RdfEvent?> run)
    {
        await run.ConfigureAwait(false);
        await Refresh().ConfigureAwait(false);
    }

    private async Task Refresh()
    {
        var list = await _client.ListAsync().ConfigureAwait(false);
        Post(() => ApplyList(list));
    }

    private async Task RefreshQuietly()
    {
        try
        {
            await Refresh().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private void ApplyList(RdfList list)
    {
        _list = list;
        foreach (var tier in list.Boxes.SelectMany(b => b.Tiers))
            if (!_tiers.Contains(tier)) _tiers.Add(tier);
        _tierIndex = Math.Clamp(_tierIndex, 0, Math.Max(0, _tiers.Count - 1));
        if (_tiers.IndexOf(RdfClient.DefaultTier) is var fast and >= 0 && !_tiersChosen)
        {
            _tierIndex = fast;
            _tiersChosen = true;
        }
        var boxes = list.Boxes
            .OrderBy(b => b.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Id, StringComparer.OrdinalIgnoreCase)
            .Select(b => new CheckRow("box:" + b.Id, "[" + b.Last.Status + "] " + b.Kind + " / " + b.Id + "  " + b.Last.Text, b.Id));
        Rows.SetRows(ActionRows().Concat(boxes));
        if (_status.StartsWith("Loading", StringComparison.Ordinal))
            _status = list.Boxes.Count + " boxes.";
    }

    private bool Begin(string label, Func<Task> work)
    {
        if (_runLabel is not null || _client.IsBusy)
        {
            Say("A check run is already in progress.");
            return false;
        }
        _runLabel = label;
        _phase = string.Empty;
        _live.Clear();
        Raise();
        _ = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Post(() => _status = "Cancelled. The box may show a stale RUNNING state until its next run.");
                await RefreshQuietly().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Post(() => _status = ex.Message);
            }
            finally
            {
                Post(() => _runLabel = null);
            }
        });
        return true;
    }

    private void Post(Action action) => _posted.Enqueue(action);

    private IReadOnlyList<MenuTextLine> BuildLines()
    {
        var lines = new List<MenuTextLine>();
        if (_runLabel is not null)
            lines.Add(new MenuTextLine("Running " + _runLabel + (_phase.Length > 0 ? " - " + _phase : "..."), LineTone.Heading));
        lines.Add(new MenuTextLine(_status, LineTone.Muted));
        if (SelectedBox is { } box)
        {
            lines.Add(new MenuTextLine(box.Title + " (" + box.Id + ")", LineTone.Heading));
            lines.Add(new MenuTextLine("Kind: " + box.Kind + (box.Tags.Count > 0 ? "   Tags: " + string.Join(", ", box.Tags) : string.Empty)));
            lines.Add(new MenuTextLine("Tiers: " + string.Join("  ", box.Tiers.Select(t => t == Tier ? "[" + t + "]" : t))));
            lines.Add(new MenuTextLine("Last: " + box.Last.Text + (box.Last.Sha is { Length: > 0 } sha ? "  " + sha : string.Empty) + (box.Last.At is { Length: > 0 } at ? "  " + at : string.Empty), ToneOf(box.Last.Status)));
            foreach (var failing in box.Last.Failing.Take(MaxFailingLines))
                lines.Add(new MenuTextLine("FAIL " + failing, LineTone.Bad));
            if (box.Last.Failing.Count > MaxFailingLines)
                lines.Add(new MenuTextLine("... " + (box.Last.Failing.Count - MaxFailingLines) + " more", LineTone.Muted));
            AddTests(lines, box);
        }
        else if (Rows.Selected is { } action)
        {
            lines.Add(new MenuTextLine(action.Text, LineTone.Heading));
        }
        if (_liveBox is null && _live.Count > 0)
        {
            lines.Add(new MenuTextLine("Live run", LineTone.Heading));
            foreach (var evt in _live.TakeLast(MaxLiveLines))
                lines.Add(new MenuTextLine(evt.Describe(), evt.IsFailure ? LineTone.Bad : LineTone.Normal));
        }
        lines.Add(new MenuTextLine("Results folder: " + (_list?.ResultsRoot is { Length: > 0 } root ? root : "unknown"), LineTone.Muted));
        return lines;
    }

    private void AddTests(List<MenuTextLine> lines, RdfBox box)
    {
        if (!_testNames.TryGetValue(box.Id, out var names) || names.Count == 0)
        {
            lines.Add(new MenuTextLine("Press Tab to list the tests of this box.", LineTone.Muted));
            return;
        }
        lines.Add(new MenuTextLine("Tests (Tab focuses one, Accept re-runs it)", LineTone.Heading));
        var start = Math.Clamp(_focus - MaxTestLines / 2, 0, Math.Max(0, names.Count - MaxTestLines));
        for (var i = start; i < Math.Min(names.Count, start + MaxTestLines); i++)
        {
            var result = _liveBox == box.Id ? _live.FirstOrDefault(e => e.Name == names[i]) : null;
            var text = (i == _focus ? "> " : "  ") + names[i] + (result is null ? string.Empty : "  " + result.Status + " " + result.Seconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "s");
            lines.Add(new MenuTextLine(text, result is null ? LineTone.Normal : result.IsFailure ? LineTone.Bad : result.IsSkip ? LineTone.Muted : LineTone.Good));
        }
    }

    private static LineTone ToneOf(string status) => status switch
    {
        "PASS" => LineTone.Good,
        "FAIL" => LineTone.Bad,
        _ => LineTone.Muted,
    };
}
