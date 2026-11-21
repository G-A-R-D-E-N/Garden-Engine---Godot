using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Service;

public enum SetupStepState
{
    Pending,
    Invalid,
    Running,
    Cancelled,
    Done,
}

public sealed class SetupStepRow
{
    public SetupStepRow(SetupStepInfo info, string value)
    {
        Info = info;
        Value = value;
    }

    public SetupStepInfo Info { get; }

    public string Value { get; set; }

    public SetupStepState State { get; set; }

    public string Message { get; set; } = string.Empty;
}

public sealed class SetupModel
{
    public const int MaxResultLines = 200;
    private const int BarWidth = 20;

    private readonly ISetupSteps _steps;
    private readonly List<SetupStepRow> _rows;
    private readonly ListMenuModel<SetupStepRow> _list;
    private ISetupRun? _run;
    private SetupStepRow? _runRow;
    private SetupStepRow? _valueRow;
    private bool _stopping;
    private IReadOnlyList<SetupItemResult> _lastResults = Array.Empty<SetupItemResult>();
    private SetupProgress? _lastProgress;

    public SetupModel(ISetupSteps steps)
    {
        _steps = steps;
        _rows = steps.Steps.Select(s => Seed(s, steps)).ToList();
        _list = new ListMenuModel<SetupStepRow>(_rows, r => r.Info.Id, r => r.Info.Title);
        View = new ServiceListView<SetupStepRow>(_list, RowText) { Extra = Handle, HintsOf = Hints, SummaryText = () => Summary };
    }

    public event Action<SetupStepRow>? ValueRequested;

    public ServiceListView<SetupStepRow> View { get; }

    public IReadOnlyList<SetupStepRow> Rows => _rows;

    public SetupStepRow? Selected => _list.Selected;

    public string Status { get; private set; } = string.Empty;

    public bool IsRunning => _run is not null;

    public bool IsStopping => _stopping;

    public bool IsComplete => _steps.IsComplete;

    public bool AllDone => _rows.All(r => r.State == SetupStepState.Done);

    public SetupProgress? Progress => _run?.Progress ?? _lastProgress;

    public double Fraction => Progress?.Fraction ?? 0;

    public IReadOnlyList<SetupItemResult> Results => _run?.Results ?? _lastResults;

    public string Summary => IsRunning
        ? (_stopping ? "Stopping after the current item" : "Running") + ", " + Percent(Fraction)
        : AllDone ? "All steps done" : FormattableString.Invariant($"{_rows.Count(r => r.State == SetupStepState.Done)} of {_rows.Count} steps done");

    public bool Activate()
    {
        if (_run is not null) return Refuse(_stopping ? "Wait for the current run to stop." : "A step is already running.");
        if (Selected is not { } row) return false;
        var blocker = _rows.TakeWhile(r => !ReferenceEquals(r, row)).FirstOrDefault(r => r.State != SetupStepState.Done);
        if (blocker is not null) return Refuse("Finish \"" + blocker.Info.Title + "\" first.");
        var check = _steps.Validate(row.Info.Id, row.Value);
        if (!check.Ok)
        {
            row.State = SetupStepState.Invalid;
            row.Message = check.Message;
            return Refuse(check.Message);
        }
        if (!row.Info.Runs)
        {
            row.State = SetupStepState.Done;
            row.Message = check.Message;
            Say("\"" + row.Info.Title + "\" confirmed.");
            CompleteIfAllDone();
            return true;
        }
        var run = _steps.Start(row.Info.Id, row.Value);
        if (run is null)
        {
            row.State = SetupStepState.Invalid;
            row.Message = "This step could not start.";
            return Refuse(row.Message);
        }
        _run = run;
        _runRow = row;
        _stopping = false;
        _lastResults = Array.Empty<SetupItemResult>();
        row.State = SetupStepState.Running;
        row.Message = string.Empty;
        Say("Started \"" + row.Info.Title + "\".");
        return true;
    }

    public bool RequestValue()
    {
        if (_run is not null || Selected is not { Info.Editable: true } row) return false;
        _valueRow = row;
        ValueRequested?.Invoke(row);
        return _valueRow is not null;
    }

    public void CancelValue(string? message = null)
    {
        _valueRow = null;
        if (message is not null) Say(message);
    }

    public bool SetValue(string value)
    {
        var row = _valueRow;
        _valueRow = null;
        if (row is null || _run is not null) return false;
        row.Value = value.Trim();
        var check = _steps.Validate(row.Info.Id, row.Value);
        row.State = check.Ok ? SetupStepState.Pending : SetupStepState.Invalid;
        row.Message = check.Message;
        var after = _rows.IndexOf(row) + 1;
        for (var i = after; i < _rows.Count; i++)
            if (_rows[i].State == SetupStepState.Done) _rows[i].State = SetupStepState.Pending;
        Say(check.Ok ? "Path accepted. Choose the step to confirm it." : check.Message);
        return check.Ok;
    }

    public bool Cancel()
    {
        if (_run is null || _stopping) return false;
        _stopping = true;
        _run.Cancel();
        Say("Stopping after the current item. Finished items are kept.");
        return true;
    }

    public bool Abort()
    {
        if (_run is null) return false;
        if (!_stopping) Cancel();
        Pump();
        if (_run is not null) Finish(true);
        return true;
    }

    public bool Pump()
    {
        if (_run is null) return false;
        var more = _run.Advance();
        if (more && !_run.Finished)
        {
            _list.Refresh();
            return true;
        }
        Finish();
        return false;
    }

    public string DetailText
    {
        get
        {
            var text = new StringBuilder();
            if (Selected is { } row)
            {
                text.AppendLine(row.Info.Title);
                text.AppendLine(row.Info.Description);
                if (row.Info.Editable) text.AppendLine("Value: " + row.Value);
                text.AppendLine("State: " + StateText(row.State));
                if (row.Message.Length > 0) text.AppendLine(row.Message);
                if (row.Info.Runs) AppendRun(text);
            }
            if (Status.Length > 0) text.AppendLine().Append(Status);
            return text.ToString().TrimEnd();
        }
    }

    public static string Bar(double fraction)
    {
        var filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * BarWidth);
        return "[" + new string('#', filled) + new string('-', BarWidth - filled) + "] " + Percent(fraction);
    }

    private static string Percent(double fraction) =>
        FormattableString.Invariant($"{(int)Math.Round(Math.Clamp(fraction, 0, 1) * 100)}%");

    private void AppendRun(StringBuilder text)
    {
        if (Progress is { } progress)
        {
            text.AppendLine(FormattableString.Invariant($"Phase {progress.PhaseIndex + 1} of {progress.PhaseCount}: {progress.Phase}, {progress.Done} of {progress.Total}"));
            if (progress.Item.Length > 0) text.AppendLine("Item: " + progress.Item);
            text.AppendLine(Bar(progress.Fraction));
        }
        var results = Results;
        if (results.Count == 0) return;
        text.AppendLine(FormattableString.Invariant($"Results ({results.Count}):"));
        var skip = Math.Max(0, results.Count - MaxResultLines);
        if (skip > 0) text.AppendLine(FormattableString.Invariant($"  ... {skip} earlier result(s) not shown"));
        foreach (var item in results.Skip(skip))
            text.AppendLine("  " + item.Item + ": " + item.State + (item.Message.Length > 0 ? " - " + item.Message : string.Empty));
    }

    private static SetupStepRow Seed(SetupStepInfo info, ISetupSteps steps)
    {
        var row = new SetupStepRow(info, steps.Detect(info.Id));
        if (!steps.IsComplete) return row;
        row.State = SetupStepState.Done;
        row.Message = "Done in an earlier setup.";
        return row;
    }

    private void Finish(bool abandoned = false)
    {
        var run = _run!;
        var row = _runRow!;
        _lastResults = run.Results.ToList();
        _lastProgress = run.Progress;
        var errors = _lastResults.Count(r => r.State == SetupItemState.Error);
        _run = null;
        _runRow = null;
        _stopping = false;
        if (run.Cancelled || abandoned)
        {
            row.State = SetupStepState.Cancelled;
            row.Message = "Cancelled. Choose this step again to resume.";
            Say("Stopped. Finished items are kept.");
            return;
        }
        row.State = SetupStepState.Done;
        row.Message = errors > 0
            ? FormattableString.Invariant($"Finished with {errors} failed item(s); the others completed.")
            : "Finished.";
        Say("\"" + row.Info.Title + "\" finished.");
        CompleteIfAllDone();
    }

    private void CompleteIfAllDone()
    {
        if (!AllDone || _steps.IsComplete) return;
        var result = _steps.MarkComplete();
        Say(result.Ok ? "Setup complete." : "Setup finished but could not be recorded: " + result.Message);
    }

    private bool Handle(MenuInputAction action)
    {
        switch (action)
        {
            case MenuInputAction.Accept: Activate(); return true;
            case MenuInputAction.Compare: RequestValue(); return true;
            case MenuInputAction.Favourite: Cancel(); return true;
            case MenuInputAction.Back:
                Cancel();
                return false;
            default: return false;
        }
    }

    private IReadOnlyList<MenuHint> Hints()
    {
        if (IsRunning)
            return [new MenuHint(MenuInputAction.Favourite, "Cancel run"), new MenuHint(MenuInputAction.Back, "Close and cancel")];
        var hints = new List<MenuHint>();
        if (Selected is { } row)
        {
            hints.Add(new MenuHint(MenuInputAction.Accept, !row.Info.Runs ? "Confirm" : row.State == SetupStepState.Cancelled ? "Resume" : "Run"));
            if (row.Info.Editable) hints.Add(new MenuHint(MenuInputAction.Compare, "Edit value"));
        }
        hints.Add(new MenuHint(MenuInputAction.Back, "Close"));
        return hints;
    }

    private string RowText(SetupStepRow row) =>
        (_rows.IndexOf(row) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ". " + row.Info.Title + " - " + StateText(row.State);

    private static string StateText(SetupStepState state) => state switch
    {
        SetupStepState.Invalid => "needs attention",
        SetupStepState.Running => "running",
        SetupStepState.Cancelled => "cancelled",
        SetupStepState.Done => "done",
        _ => "not done",
    };

    private bool Refuse(string status)
    {
        Say(status);
        return false;
    }

    private void Say(string status)
    {
        Status = status;
        _list.Refresh();
    }
}
