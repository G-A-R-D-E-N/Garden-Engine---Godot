using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Terminals;

public enum HackingPhase
{
    Typing,
    Playing,
    Accepted,
    LockedOut,
    Closed,
}

public sealed class HackingMenuModel
{
    private readonly IHackingSession _session;
    private readonly HackingOptions _options;
    private readonly HackingGeometry _geometry;
    private readonly TypeWriter _typing;
    private readonly List<string> _log = new();
    private int _revealed;
    private double _hold;

    public HackingMenuModel(IHackingSession session, HackingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _options = options ?? new HackingOptions();
        _geometry = session.Geometry;
        _typing = new TypeWriter(_options.CharsPerSecond);
    }

    public event Action? Changed;

    public event Action<HackingOutcome>? Finished;

    public event Action<HackingGuessKind>? HelpUsed;

    public event Action<string>? SoundRequested;

    public HackingPhase Phase { get; private set; } = HackingPhase.Typing;

    public HackingGeometry Geometry => _geometry;

    public int Cursor { get; private set; } = -1;

    public int RevealedCells => _revealed;

    public IReadOnlyList<string> Log => _log;

    public int AttemptsRemaining => _session.AttemptsRemaining;

    public int MaxAttempts => _session.MaxAttempts;

    public string AttemptsText => $"{_session.AttemptsRemaining} {_options.Strings.Attempts}";

    public HackingHit? Highlight => Phase == HackingPhase.Playing && Cursor >= 0 ? _session.HitAt(Cursor) : null;

    public string Prompt => Highlight is { } hit ? TextOf(hit) : string.Empty;

    public char CellAt(int index) => _session.CellAt(index);

    public IReadOnlyList<MenuHint> Hints { get; } = new[]
    {
        new MenuHint(MenuInputAction.Accept, "Select"),
        new MenuHint(MenuInputAction.Back, "Exit"),
    };

    public void Tick(double seconds)
    {
        if (Phase == HackingPhase.Closed || !double.IsFinite(seconds) || seconds <= 0)
            return;
        if (!_session.StillValid)
        {
            Finish(HackingOutcome.Cancelled);
            return;
        }
        switch (Phase)
        {
            case HackingPhase.Typing:
                int count = _typing.Advance(seconds);
                if (count > 0)
                {
                    _revealed = Math.Min(_geometry.Cells, _revealed + count);
                    if (_revealed >= _geometry.Cells)
                        EndTyping();
                    Raise();
                }
                break;
            case HackingPhase.Accepted:
                _hold += seconds;
                if (_hold >= _options.AcceptedHold)
                    Finish(HackingOutcome.Accepted);
                break;
            case HackingPhase.LockedOut:
                _hold += seconds;
                if (_hold >= _options.LockedOutHold)
                    Finish(HackingOutcome.LockedOut);
                break;
        }
    }

    public bool Handle(MenuInputAction action)
    {
        if (Phase == HackingPhase.Closed)
            return false;
        if (Phase is HackingPhase.Accepted or HackingPhase.LockedOut)
            return true;
        if (action == MenuInputAction.Back)
        {
            Finish(HackingOutcome.Cancelled);
            return true;
        }
        if (Phase == HackingPhase.Typing)
        {
            if (action == MenuInputAction.Accept)
            {
                Sound("type_skip");
                EndTyping();
                Raise();
            }
            return true;
        }
        switch (action)
        {
            case MenuInputAction.Left:
            case MenuInputAction.Right:
            case MenuInputAction.Up:
            case MenuInputAction.Down:
                MoveCursor(action);
                return true;
            case MenuInputAction.Accept:
                Guess();
                return true;
            default:
                return false;
        }
    }

    public void Cancel() => Finish(HackingOutcome.Cancelled);

    public bool Hover(int index)
    {
        if (Phase != HackingPhase.Playing || index < 0 || index >= _geometry.Cells)
            return false;
        SetCursor(index);
        return true;
    }

    public string TextOf(HackingHit hit)
    {
        var chars = new char[hit.Length];
        for (int i = 0; i < hit.Length; i++)
            chars[i] = _session.CellAt(hit.Start + i);
        return new string(chars);
    }

    private void EndTyping()
    {
        _revealed = _geometry.Cells;
        Phase = HackingPhase.Playing;
        Cursor = 0;
    }

    private void MoveCursor(MenuInputAction action)
    {
        int width = _geometry.CellsPerRow;
        int perColumn = _geometry.CellsPerColumn;
        int column = Cursor / perColumn;
        int within = Cursor % perColumn;
        int row = within / width;
        int x = within % width;
        int next = Cursor;
        switch (action)
        {
            case MenuInputAction.Left:
                if (x > 0)
                    next = Cursor - 1;
                else if (column > 0)
                    next = (column - 1) * perColumn + row * width + width - 1;
                break;
            case MenuInputAction.Right:
                if (x < width - 1)
                    next = Cursor + 1;
                else if (column < _geometry.Columns - 1)
                    next = (column + 1) * perColumn + row * width;
                break;
            case MenuInputAction.Up:
                if (row > 0)
                    next = Cursor - width;
                break;
            case MenuInputAction.Down:
                if (row < _geometry.RowsPerColumn - 1)
                    next = Cursor + width;
                break;
        }
        SetCursor(next);
    }

    private void SetCursor(int index)
    {
        if (index == Cursor)
            return;
        Cursor = index;
        if (_session.HitAt(index) is not null)
            Sound("hover_word");
        Raise();
    }

    private void Guess()
    {
        if (Highlight is not { } hit)
            return;
        string word = TextOf(hit);
        HackingGuess guess = _session.Select(hit);
        HackingStrings words = _options.Strings;
        switch (guess.Kind)
        {
            case HackingGuessKind.Correct:
                AddLog(word);
                AddLog(words.Granted);
                Phase = HackingPhase.Accepted;
                _hold = 0;
                Sound("password_good");
                break;
            case HackingGuessKind.Wrong:
                AddLog(word);
                AddLog(words.Denied);
                AddLog(string.Format(words.Likeness, guess.Likeness));
                break;
            case HackingGuessKind.LockedOut:
                AddLog(word);
                AddLog(words.Denied);
                AddLog(words.LockedOut);
                Phase = HackingPhase.LockedOut;
                _hold = 0;
                Sound("password_bad");
                break;
            case HackingGuessKind.DudRemoved:
                AddLog(words.DudRemoved);
                Sound("help_dud");
                HelpUsed?.Invoke(guess.Kind);
                break;
            case HackingGuessKind.AttemptsRestored:
                AddLog(words.Restored);
                Sound("help_attempts");
                HelpUsed?.Invoke(guess.Kind);
                break;
            default:
                return;
        }
        Sound("terminal.accept");
        Raise();
    }

    private void AddLog(string message)
    {
        _log.AddRange(TextWrap.Wrap(message, _options.LogColumns, line => line.Length));
        if (_log.Count > _options.LogLines)
            _log.RemoveRange(0, _log.Count - _options.LogLines);
    }

    private void Finish(HackingOutcome outcome)
    {
        if (Phase == HackingPhase.Closed)
            return;
        Phase = HackingPhase.Closed;
        Finished?.Invoke(outcome);
        Raise();
    }

    private void Sound(string name) => SoundRequested?.Invoke(name);

    private void Raise() => Changed?.Invoke();
}
