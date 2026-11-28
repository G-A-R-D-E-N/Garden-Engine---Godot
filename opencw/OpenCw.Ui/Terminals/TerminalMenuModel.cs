using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Terminals;

public sealed record TerminalOptions
{
    public double CharsPerSecond { get; init; } = 60;
    public int Columns { get; init; } = 72;
    public int PageLines { get; init; } = 14;
    public int VisibleRows { get; init; } = 12;
    public double BusyMinimum { get; init; } = 0.5;
    public double BusyMaximum { get; init; } = 5.0;
    public string DefaultHeader { get; init; } = "TERMINAL";
    public string ImageText { get; init; } = "This entry shows an image that cannot be shown here.";
    public string UnavailableText { get; init; } = "Unavailable";
    public string UnsupportedText { get; init; } = "Unsupported action";
}

public enum TerminalPhase
{
    Typing,
    Ready,
    Busy,
    Closed,
}

public sealed class TerminalMenuModel
{
    private const int FirstEntrySegment = 3;

    private static readonly IReadOnlyList<TerminalEntry> NoEntries = Array.Empty<TerminalEntry>();

    private readonly ITerminalSource _source;
    private readonly string _id;
    private readonly string _root;
    private readonly TerminalOptions _options;
    private readonly List<string> _stack = new();
    private readonly Dictionary<string, int> _memory = new();
    private readonly List<(string Text, bool Skip)> _segments = new();
    private readonly TypeWriter _typing;
    private readonly TypeWriter _responseTyping;
    private string _nodeId;
    private TerminalNode? _node;
    private IReadOnlyList<string> _pages = new[] { string.Empty };
    private int _page;
    private bool _text;
    private string _header = string.Empty;
    private string _welcome = string.Empty;
    private string? _shownHeader;
    private string? _shownWelcome;
    private int _revealed;
    private int _total;
    private bool _looping;
    private int _cursor = -1;
    private int _scroll;
    private TerminalEntry? _pending;
    private double _busy;
    private int _responseShown;

    public TerminalMenuModel(ITerminalSource source, string terminalId, string rootNode, TerminalOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(terminalId);
        ArgumentException.ThrowIfNullOrEmpty(rootNode);
        _source = source;
        _id = terminalId;
        _root = rootNode;
        _nodeId = rootNode;
        _options = options ?? new TerminalOptions();
        _typing = new TypeWriter(_options.CharsPerSecond);
        _responseTyping = new TypeWriter(_options.CharsPerSecond);
        Redraw();
    }

    public event Action? Changed;

    public event Action? Closed;

    public event Action<string>? SoundRequested;

    public TerminalPhase Phase { get; private set; } = TerminalPhase.Typing;

    public string TerminalId => _id;

    public string NodeId => _nodeId;

    public int Depth => _stack.Count;

    public int Page => _page;

    public int PageCount => _pages.Count;

    public bool IsTyping => Phase == TerminalPhase.Typing;

    public bool IsBusy => Phase == TerminalPhase.Busy;

    public bool IsShowingText => _text;

    public bool EntriesVisible => Phase != TerminalPhase.Closed && !_text && _node is not null && _page == _pages.Count - 1;

    public IReadOnlyList<TerminalEntry> Entries => EntriesVisible ? _node!.Entries : NoEntries;

    public int SelectedIndex => EntriesVisible ? _cursor : -1;

    public int ScrollTop => _scroll;

    public int VisibleRows => _options.VisibleRows;

    public string Header => Reveal(0);

    public string Welcome => Reveal(1);

    public string Body => Reveal(2);

    public string EntryText(int index) => index >= 0 && index < Entries.Count ? Reveal(FirstEntrySegment + index) : string.Empty;

    public bool IsEntryAvailable(int index) => index >= 0 && index < Entries.Count && Block(Entries[index]) is null;

    public string Prompt
    {
        get
        {
            if (Phase == TerminalPhase.Busy)
                return (_pending?.Response ?? string.Empty)[.._responseShown];
            if (Phase != TerminalPhase.Closed && _cursor >= 0 && EntriesVisible && Block(_node!.Entries[_cursor]) is { } reason)
                return reason;
            return string.Empty;
        }
    }

    public IReadOnlyList<MenuHint> Hints
    {
        get
        {
            var hints = new List<MenuHint> { new(MenuInputAction.Accept, "Accept") };
            if (PageCount > 1)
            {
                hints.Add(new MenuHint(MenuInputAction.PageUp, "Page up"));
                hints.Add(new MenuHint(MenuInputAction.PageDown, "Page down"));
            }
            hints.Add(new MenuHint(MenuInputAction.Back, Depth > 0 || _text ? "Back" : "Exit"));
            return hints;
        }
    }

    public void Tick(double seconds)
    {
        if (Phase == TerminalPhase.Closed || !double.IsFinite(seconds) || seconds <= 0)
            return;
        if (!_source.IsValid(_id))
        {
            Close();
            return;
        }
        if (Phase == TerminalPhase.Typing)
        {
            int count = _typing.Advance(seconds);
            if (count == 0)
                return;
            _revealed += count;
            if (_revealed >= _total)
                EndTyping();
            Raise();
        }
        else if (Phase == TerminalPhase.Busy)
        {
            _busy += seconds;
            string response = _pending?.Response ?? string.Empty;
            int count = _responseTyping.Advance(seconds);
            _responseShown = Math.Min(response.Length, _responseShown + count);
            bool typed = _responseShown >= response.Length;
            if ((typed && _busy >= _options.BusyMinimum) || _busy >= _options.BusyMaximum)
                Apply();
            else if (count > 0)
                Raise();
        }
    }

    public bool Handle(MenuInputAction action)
    {
        if (Phase == TerminalPhase.Closed)
            return false;
        if (!_source.IsValid(_id))
        {
            Close();
            return true;
        }
        if (Phase == TerminalPhase.Busy)
        {
            if (action == MenuInputAction.Back && Depth == 0)
                Close();
            return true;
        }
        if (Phase == TerminalPhase.Typing)
        {
            if (action is MenuInputAction.Accept or MenuInputAction.Back)
            {
                if (action == MenuInputAction.Accept)
                    Sound("type_skip");
                EndTyping();
                Raise();
            }
            return true;
        }
        switch (action)
        {
            case MenuInputAction.Up:
                Move(-1);
                return true;
            case MenuInputAction.Down:
                Move(1);
                return true;
            case MenuInputAction.Left:
            case MenuInputAction.PageUp:
                ShowPage(_page - 1);
                return true;
            case MenuInputAction.Right:
            case MenuInputAction.PageDown:
                ShowPage(_page + 1);
                return true;
            case MenuInputAction.Accept:
                AcceptReady();
                return true;
            case MenuInputAction.Back:
                BackReady();
                return true;
            default:
                return false;
        }
    }

    public bool Select(int index)
    {
        if (Phase != TerminalPhase.Ready || !EntriesVisible || index < 0 || index >= _node!.Entries.Count)
            return false;
        if (index != _cursor)
        {
            _cursor = index;
            _memory[_nodeId] = index;
            EnsureVisible();
            Sound("select_move");
            Raise();
        }
        return true;
    }

    public bool Activate(int index)
    {
        if (!Select(index))
            return false;
        TerminalEntry entry = _node!.Entries[index];
        if (Block(entry) is not null)
            return false;
        _memory[_nodeId] = index;
        Sound("terminal.accept");
        Sound("store_a");
        Sound("store_b");
        _source.EntryRun(_id, _nodeId, entry.Id);
        _pending = entry;
        _busy = 0;
        _responseShown = 0;
        _responseTyping.Reset();
        Phase = TerminalPhase.Busy;
        Raise();
        return true;
    }

    public void Close()
    {
        if (Phase == TerminalPhase.Closed)
            return;
        StopLoop();
        Phase = TerminalPhase.Closed;
        _pending = null;
        Closed?.Invoke();
        Raise();
    }

    private void AcceptReady()
    {
        if (_page < _pages.Count - 1)
            ShowPage(_page + 1);
        else if (_text)
        {
            Redraw();
            Raise();
        }
        else
            Activate(_cursor);
    }

    private void BackReady()
    {
        if (_text)
            Redraw();
        else if (_stack.Count > 0)
        {
            _nodeId = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            Redraw();
        }
        else
        {
            Close();
            return;
        }
        Raise();
    }

    private void Apply()
    {
        TerminalEntry entry = _pending!;
        _pending = null;
        Phase = TerminalPhase.Ready;
        switch (entry.Kind)
        {
            case TerminalEntryKind.Submenu:
                _stack.Add(_nodeId);
                _nodeId = entry.TargetNode!;
                Redraw();
                break;
            case TerminalEntryKind.ReturnToTop:
                _stack.Clear();
                _nodeId = _root;
                Redraw();
                break;
            case TerminalEntryKind.Text:
                ShowText(entry.PageText ?? string.Empty);
                break;
            case TerminalEntryKind.Image:
                ShowText(_options.ImageText);
                break;
            default:
                Redraw();
                break;
        }
        Raise();
    }

    private bool Redraw()
    {
        TerminalNode? node = _source.Node(_id, _nodeId);
        if (node is null)
        {
            Close();
            return false;
        }
        _node = node;
        _text = false;
        _header = string.IsNullOrEmpty(node.Header) ? _options.DefaultHeader : Normalize(node.Header);
        _welcome = Normalize(node.Welcome);
        _pages = Paginate(node.Body);
        _page = 0;
        _cursor = node.Entries.Count == 0 ? -1 : Math.Clamp(_memory.GetValueOrDefault(_nodeId, 0), 0, node.Entries.Count - 1);
        _scroll = 0;
        EnsureVisible();
        BeginTyping();
        return true;
    }

    private void ShowText(string text)
    {
        _text = true;
        _pages = Paginate(text);
        _page = 0;
        BeginTyping();
    }

    private void ShowPage(int page)
    {
        if (page < 0 || page >= _pages.Count)
            return;
        _page = page;
        BeginTyping();
        Raise();
    }

    private void BeginTyping()
    {
        StopLoop();
        _segments.Clear();
        _segments.Add((_header, _header == _shownHeader));
        _segments.Add((_welcome, _welcome == _shownWelcome));
        _segments.Add((_pages[_page], false));
        if (EntriesVisible)
        {
            foreach (TerminalEntry entry in _node!.Entries)
                _segments.Add((entry.Text ?? string.Empty, false));
        }
        _total = 0;
        foreach (var segment in _segments)
            _total += segment.Skip ? 0 : segment.Text.Length;
        _revealed = 0;
        _typing.Reset();
        Phase = TerminalPhase.Typing;
        if (_total == 0)
        {
            EndTyping();
            return;
        }
        _looping = true;
        Sound("type_loop_start");
    }

    private void EndTyping()
    {
        _revealed = _total;
        _shownHeader = _header;
        _shownWelcome = _welcome;
        StopLoop();
        if (Phase == TerminalPhase.Typing)
            Phase = TerminalPhase.Ready;
    }

    private void StopLoop()
    {
        if (!_looping)
            return;
        _looping = false;
        Sound("type_loop_stop");
    }

    private string Reveal(int segment)
    {
        if (segment < 0 || segment >= _segments.Count)
            return string.Empty;
        int offset = 0;
        for (int i = 0; i < segment; i++)
            offset += _segments[i].Skip ? 0 : _segments[i].Text.Length;
        var (text, skip) = _segments[segment];
        if (skip)
            return text;
        return text[..Math.Clamp(_revealed - offset, 0, text.Length)];
    }

    private void Move(int delta)
    {
        if (!EntriesVisible || _node!.Entries.Count == 0)
            return;
        int count = _node.Entries.Count;
        _cursor = ((_cursor + delta) % count + count) % count;
        _memory[_nodeId] = _cursor;
        EnsureVisible();
        Sound("select_move");
        Raise();
    }

    private void EnsureVisible()
    {
        if (_cursor < 0)
            return;
        if (_cursor < _scroll)
            _scroll = _cursor;
        else if (_cursor >= _scroll + _options.VisibleRows)
            _scroll = _cursor - _options.VisibleRows + 1;
    }

    private string? Block(TerminalEntry entry)
    {
        if (entry.Unavailable is not null)
            return entry.Unavailable.Length == 0 ? _options.UnavailableText : entry.Unavailable;
        if (entry.Kind == TerminalEntryKind.Unsupported)
            return _options.UnsupportedText;
        if (entry.Kind == TerminalEntryKind.Submenu && string.IsNullOrEmpty(entry.TargetNode))
            return _options.UnavailableText;
        return null;
    }

    private IReadOnlyList<string> Paginate(string text)
    {
        var lines = TextWrap.Wrap(Normalize(text), _options.Columns, line => line.Length);
        if (lines.Count == 0)
            return new[] { string.Empty };
        var pages = new List<string>();
        for (int i = 0; i < lines.Count; i += _options.PageLines)
            pages.Add(string.Join('\n', lines.Skip(i).Take(_options.PageLines)));
        return pages;
    }

    private static string Normalize(string? text) => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

    private void Sound(string name) => SoundRequested?.Invoke(name);

    private void Raise() => Changed?.Invoke();
}
