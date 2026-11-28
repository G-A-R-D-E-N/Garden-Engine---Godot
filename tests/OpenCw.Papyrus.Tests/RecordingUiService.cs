using System;
using System.Collections.Generic;
using System.Threading;
using OpenCommonwealth.Services.Papyrus.Ui;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCw.Papyrus.Tests;

// Invariant: a test double that records what scripts asked the UI service for.
public sealed class RecordingUiService : IUiService
{
    private readonly List<string> _notifications = new();
    private readonly List<string> _messages = new();
    private readonly List<string> _terminal = new();
    private readonly List<string> _shown = new();
    private readonly List<Action<int>> _answers = new();
    private readonly List<CancellationToken> _cancellations = new();
    private readonly List<string> _menus = new();
    private readonly Dictionary<(string Menu, string Path), PapyrusValue> _values = new();
    private readonly List<(string Menu, string Path, PapyrusValue Value)> _sets = new();
    private readonly List<(string Menu, string Path, int Count)> _invocations = new();

    public IReadOnlyList<string> Notifications => _notifications;

    public IReadOnlyList<string> Messages => _messages;

    public IReadOnlyList<string> TerminalLines => _terminal;

    public IReadOnlyList<string> Shown => _shown;

    public IReadOnlyList<Action<int>> Answers => _answers;

    public IReadOnlyList<CancellationToken> Cancellations => _cancellations;

    public IReadOnlyList<string> Menus => _menus;

    public IReadOnlyList<(string Menu, string Path, PapyrusValue Value)> Sets => _sets;

    public IReadOnlyList<(string Menu, string Path, int Count)> Invocations => _invocations;

    public int? AnswerAtOnce { get; set; }

    public Exception? ShowFailure { get; set; }

    public void Notify(string text) => _notifications.Add(text);

    public void MessageBox(string text) => _messages.Add(text);

    public void ShowMessage(string text, Action<int> answer, CancellationToken cancelled)
    {
        if (ShowFailure is { } failure) throw failure;
        _shown.Add(text);
        _answers.Add(answer);
        _cancellations.Add(cancelled);
        if (AnswerAtOnce is { } choice) answer(choice);
    }

    public void Terminal(string text) => _terminal.Add(text);

    public bool OpenMenu(string menu)
    {
        _menus.Add(menu);
        return true;
    }

    public bool IsMenuOpen(string menu) => _menus.Contains(menu);

    public PapyrusValue Get(string menu, string path) =>
        _values.TryGetValue((menu, path), out var value) ? value : PapyrusValue.None;

    public void Set(string menu, string path, PapyrusValue value)
    {
        _sets.Add((menu, path, value));
        _values[(menu, path)] = value;
    }

    public PapyrusValue Invoke(string menu, string path, IReadOnlyList<PapyrusValue> arguments)
    {
        _invocations.Add((menu, path, arguments.Count));
        return PapyrusValue.FromInt(arguments.Count);
    }
}
