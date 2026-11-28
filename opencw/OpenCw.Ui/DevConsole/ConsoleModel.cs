using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Dialogs;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.DevConsole;

public sealed class ConsoleModel
{
    public const int MaxLines = 500;
    public const int MaxHistory = 50;
    public const int MaxInput = 512;
    public const string NoCommands = "No commands available.";
    public const string NoTarget = "none";
    public const string EchoPrefix = "> ";
    public const string TargetCleared = "Target cleared.";

    private static readonly TextPromptOptions InputOptions = new() { MaxLength = MaxInput, AllowEmpty = true };

    private readonly IConsoleHost _host;
    private readonly List<ConsoleLine> _lines = new();
    private readonly List<string> _history = new();
    private readonly Queue<string> _pending = new();
    private readonly ConcurrentQueue<ConsoleLine> _incoming = new();
    private TextPromptModel _input = new(null, InputOptions);
    private Completion? _completion;
    private string? _draft;
    private int _cursor = -1;
    private int _offset;
    private int _viewLines = 12;

    public ConsoleModel(IConsoleHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    public event Action? Changed;

    public event Action? Finished;

    public TextPromptModel Input => _input;

    public IReadOnlyList<ConsoleLine> Lines => _lines;

    public IReadOnlyList<string> History => _history;

    public ConsoleTarget? Target { get; private set; }

    public string TargetText => "Target: " + (Target?.Name ?? NoTarget);

    public bool IsOpen { get; private set; } = true;

    public int ScrollOffset => _offset;

    public bool HasMoreBelow => _offset > 0;

    public IReadOnlyList<ConsoleLine> VisibleLines
    {
        get
        {
            var end = _lines.Count - _offset;
            var start = Math.Max(0, end - _viewLines);
            return _lines.GetRange(start, end - start);
        }
    }

    private int MaxOffset => Math.Max(0, _lines.Count - _viewLines);

    public void Open()
    {
        IsOpen = true;
        _offset = 0;
        _cursor = -1;
        _draft = null;
        SetInput(string.Empty);
        Notify();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Finished?.Invoke();
        Notify();
    }

    public void SetViewLines(int lines)
    {
        lines = Math.Max(1, lines);
        if (lines == _viewLines) return;
        _viewLines = lines;
        _offset = Math.Min(_offset, MaxOffset);
        Notify();
    }

    public void Append(ConsoleSeverity severity, string text)
    {
        _incoming.Enqueue(new ConsoleLine(severity, Clean(text)));
        while (_incoming.Count > MaxLines)
            _incoming.TryDequeue(out _);
    }

    public void Tick(double deltaSeconds)
    {
        var changed = false;
        while (_incoming.TryDequeue(out var line))
        {
            Add(line);
            changed = true;
        }
        while (_pending.Count > 0)
        {
            Run(_pending.Dequeue());
            changed = true;
        }
        if (changed) Notify();
    }

    public string EditText(string? text, int caretUtf16)
    {
        var clean = _input.ReplaceAll(text, caretUtf16);
        Notify();
        return clean;
    }

    public bool Submit()
    {
        var line = _input.Result;
        if (!IsOpen || line.Length == 0) return false;
        Add(new ConsoleLine(ConsoleSeverity.Echo, EchoPrefix + Clean(line)));
        _offset = 0;
        _history.Remove(line);
        _history.Add(line);
        if (_history.Count > MaxHistory) _history.RemoveAt(0);
        _cursor = -1;
        _draft = null;
        _pending.Enqueue(line);
        SetInput(string.Empty);
        Notify();
        return true;
    }

    public bool HistoryPrevious()
    {
        if (_history.Count == 0) return false;
        if (_cursor < 0)
        {
            _draft = _input.Value;
            _cursor = _history.Count - 1;
        }
        else if (_cursor > 0)
        {
            _cursor--;
        }
        SetInput(_history[_cursor]);
        Notify();
        return true;
    }

    public bool HistoryNext()
    {
        if (_cursor < 0) return false;
        if (_cursor < _history.Count - 1)
        {
            _cursor++;
            SetInput(_history[_cursor]);
        }
        else
        {
            _cursor = -1;
            SetInput(_draft ?? string.Empty);
            _draft = null;
        }
        Notify();
        return true;
    }

    public bool Complete()
    {
        var value = _input.Value;
        if (_completion is { } active && active.Text == value)
            return Apply(active with { Index = (active.Index + 1) % active.Matches.Count });
        var head = value[.._input.Caret];
        var start = head.LastIndexOf(' ') + 1;
        List<string>? matches;
        try
        {
            matches = _host.Complete(head[start..])?.Where(match => !string.IsNullOrEmpty(match)).ToList();
        }
        catch (Exception ex)
        {
            Add(new ConsoleLine(ConsoleSeverity.Error, Clean(ex.Message)));
            Notify();
            return false;
        }
        if (matches is null || matches.Count == 0)
        {
            _completion = null;
            return false;
        }
        return Apply(new Completion(matches, value[..start], value[_input.Caret..], 0, value));
    }

    public void Scroll(int lines)
    {
        var next = Math.Clamp(_offset + lines, 0, MaxOffset);
        if (next == _offset) return;
        _offset = next;
        Notify();
    }

    public void ScrollPage(int direction) => Scroll(direction * Math.Max(1, _viewLines - 1));

    public void ScrollToStart() => Scroll(MaxOffset);

    public void ScrollToEnd() => Scroll(-_offset);

    public void PickAt(double x, double y)
    {
        ConsoleTarget? picked;
        try
        {
            picked = _host.Pick(x, y);
        }
        catch (Exception ex)
        {
            Add(new ConsoleLine(ConsoleSeverity.Error, Clean(ex.Message)));
            Notify();
            return;
        }
        if (picked is null && Target is null) return;
        Target = picked;
        Add(new ConsoleLine(ConsoleSeverity.Info, picked is null ? TargetCleared : "Picked " + Clean(picked.Name) + "."));
        Notify();
    }

    public bool Handle(MenuInputAction action)
    {
        switch (action)
        {
            case MenuInputAction.Up:
                HistoryPrevious();
                return true;
            case MenuInputAction.Down:
                HistoryNext();
                return true;
            case MenuInputAction.PageUp:
                ScrollPage(1);
                return true;
            case MenuInputAction.PageDown:
                ScrollPage(-1);
                return true;
            case MenuInputAction.TabNext:
                Complete();
                return true;
            case MenuInputAction.Accept:
                Submit();
                return true;
            case MenuInputAction.Back:
                Close();
                return true;
            default:
                return false;
        }
    }

    private bool Apply(Completion next)
    {
        var word = next.Matches[next.Index];
        _input.ReplaceAll(next.Head + word + next.Tail, next.Head.Length + word.Length);
        _completion = next with { Text = _input.Value };
        Notify();
        return true;
    }

    private void Run(string line)
    {
        if (line.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            Help();
            return;
        }
        if (line.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            _lines.Clear();
            _offset = 0;
            return;
        }
        try
        {
            var result = _host.Execute(line, Target);
            if (result is null) return;
            foreach (var entry in result.Lines ?? Array.Empty<ConsoleLine>())
                Add(new ConsoleLine(entry.Severity, Clean(entry.Text)));
            if (result.Target is not null) Target = result.Target;
        }
        catch (Exception ex)
        {
            Add(new ConsoleLine(ConsoleSeverity.Error, Clean(ex.Message)));
        }
    }

    private void Help()
    {
        try
        {
            var names = _host.Complete(string.Empty);
            Add(new ConsoleLine(ConsoleSeverity.Info, names is null || names.Count == 0 ? NoCommands : Clean(string.Join(", ", names))));
        }
        catch (Exception ex)
        {
            Add(new ConsoleLine(ConsoleSeverity.Error, Clean(ex.Message)));
        }
    }

    private void Add(ConsoleLine line)
    {
        _lines.Add(line);
        if (_offset > 0) _offset++;
        if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
        _offset = Math.Min(_offset, MaxOffset);
    }

    private void SetInput(string text) => _input = new TextPromptModel(text, InputOptions);

    private void Notify() => Changed?.Invoke();

    private static string Clean(string? text) =>
        text is null ? string.Empty : new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());

    private sealed record Completion(IReadOnlyList<string> Matches, string Head, string Tail, int Index, string Text);
}
