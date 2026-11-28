using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.TextTags;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using TerminalItemType = Radicle.Interop.Native.TerminalItemType;

namespace OpenCommonwealth.Services.Ui.Terminals;

public enum TerminalLockState
{
    Unlocked,
    Password,
    Refused,
}

public sealed record TerminalPlacement(string BaseKey, TerminalLockState Lock, int HackLevel = 0, string RefusedReason = "", bool HasKey = false);

public interface ITerminalRecords
{
    TerminalRecord? Terminal(string baseKey);

    TerminalPlacement? Placement(string refKey);
}

public interface ITerminalConditions
{
    RuleVerdict Evaluate(IReadOnlyList<RecordCondition> conditions);
}

public sealed record TerminalEntryRun(string RefKey, string TerminalKey, ushort? ItemId);

public sealed record RecordTerminalOptions
{
    public const string LockedOutText = "This terminal has locked you out";
    public const string NoWordsText = "No hacking words are available for this terminal.";

    public double LockoutSeconds { get; init; } = 10;
    public float Intelligence { get; init; } = 5f;
    public float BaseGuesses { get; init; } = HackingRules.BaseGuesses;
}

public sealed class RecordTerminalSource : ITerminalSource
{
    private readonly ITerminalRecords _records;
    private readonly IHackingWords _words;
    private readonly Func<double> _seconds;
    private readonly Func<int> _seed;
    private readonly Func<string, bool> _isValid;
    private readonly RecordTerminalOptions _options;
    private readonly ContainerLedger _ledger;
    private readonly ITerminalConditions? _conditions;
    private readonly Func<string, ITagResolver?>? _tags;
    private readonly HashSet<string> _recordedAtGuess = new(StringComparer.Ordinal);

    public RecordTerminalSource(
        ITerminalRecords records,
        IHackingWords words,
        Func<double> seconds,
        Func<int>? seed = null,
        Func<string, bool>? isValid = null,
        RecordTerminalOptions? options = null,
        ContainerLedger? ledger = null,
        ITerminalConditions? conditions = null,
        Func<string, ITagResolver?>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(seconds);
        _records = records;
        _words = words;
        _seconds = seconds;
        _seed = seed ?? (() => Random.Shared.Next());
        _isValid = isValid ?? (_ => true);
        _options = options ?? new RecordTerminalOptions();
        _ledger = ledger ?? new ContainerLedger();
        _conditions = conditions;
        _tags = tags;
    }

    public event Action<TerminalEntryRun>? EntryRunRequested;

    public TerminalOpening Open(string terminalId)
    {
        if (_records.Placement(terminalId) is not { } placement || _records.Terminal(placement.BaseKey) is null)
            return new TerminalOpening(TerminalOpenStatus.Unavailable, Reason: TerminalFlowModel.DefaultFailure);
        if (placement.Lock == TerminalLockState.Unlocked || _ledger.IsUnlocked(terminalId))
            return new TerminalOpening(TerminalOpenStatus.Open, placement.BaseKey);
        if (placement.HasKey)
        {
            _ledger.MarkUnlocked(terminalId);
            return new TerminalOpening(TerminalOpenStatus.Open, placement.BaseKey);
        }
        if (_ledger.LockedOutUntil(terminalId) is { } until && _seconds() < until)
            return new TerminalOpening(TerminalOpenStatus.LockedOut, Reason: RecordTerminalOptions.LockedOutText);
        if (placement.Lock == TerminalLockState.Refused)
            return new TerminalOpening(TerminalOpenStatus.Unavailable, Reason: placement.RefusedReason);
        int idBit = RecordKey.TryParse(terminalId, out _, out uint local) ? (int)(local & 1) : 0;
        int length = HackingRules.WordLength(placement.HackLevel, idBit);
        IReadOnlyList<string> pool = _words.OfLength(length);
        if (pool.Count < 2)
            return new TerminalOpening(TerminalOpenStatus.Unavailable, Reason: RecordTerminalOptions.NoWordsText);
        var board = new HackingBoard(pool, HackingRules.WordCount(_options.Intelligence), HackingRules.Guesses(_options.BaseGuesses), _seed());
        return new TerminalOpening(TerminalOpenStatus.Locked, placement.BaseKey, Hack: new Recording(board, outcome => { _recordedAtGuess.Add(terminalId); Record(terminalId, outcome); }));
    }

    public TerminalNode? Node(string terminalId, string nodeId)
    {
        if (_records.Terminal(nodeId) is not { } term)
            return null;
        var tags = _tags?.Invoke(terminalId);
        string body = term.BodyTexts.FirstOrDefault(b => Verdict(b.ConditionCount, b.Conditions) == RuleVerdict.True)?.Text.Text is { } text ? TagExpander.Expand(Clean(text), tags) : string.Empty;
        var entries = new List<TerminalEntry>(term.MenuItems.Count);
        for (int i = 0; i < term.MenuItems.Count; i++)
        {
            var verdict = Verdict(term.MenuItems[i].ConditionCount, term.MenuItems[i].Conditions);
            if (verdict != RuleVerdict.False)
                entries.Add(Entry(i, term.MenuItems[i], verdict == RuleVerdict.True, tags));
        }
        string header = Clean(term.HeaderText.Text ?? term.FullName.Text);
        return new TerminalNode(nodeId, header, Clean(term.WelcomeText.Text), body, entries);
    }

    public bool IsValid(string terminalId) => _isValid(terminalId);

    public void EntryRun(string terminalId, string nodeId, string entryId)
    {
        ushort? item = _records.Terminal(nodeId) is { } term && int.TryParse(entryId, out int index) && index >= 0 && index < term.MenuItems.Count
            ? term.MenuItems[index].ItemId
            : null;
        EntryRunRequested?.Invoke(new TerminalEntryRun(terminalId, nodeId, item));
    }

    public void HackFinished(string terminalId, HackingOutcome outcome)
    {
        if (!_recordedAtGuess.Remove(terminalId))
            Record(terminalId, outcome);
    }

    private void Record(string terminalId, HackingOutcome outcome)
    {
        if (outcome == HackingOutcome.Accepted)
            _ledger.MarkUnlocked(terminalId);
        else if (outcome == HackingOutcome.LockedOut)
        {
            double now = _seconds();
            if (_ledger.LockedOutUntil(terminalId) is not { } until || now >= until)
                _ledger.LockOut(terminalId, now + _options.LockoutSeconds);
        }
    }

    internal sealed class Recording(IHackingSession inner, Action<HackingOutcome> record) : IHackingSession
    {
        internal IHackingSession Inner => inner;

        public HackingGeometry Geometry => inner.Geometry;
        public int MaxAttempts => inner.MaxAttempts;
        public int AttemptsRemaining => inner.AttemptsRemaining;
        public bool StillValid => inner.StillValid;
        public char CellAt(int index) => inner.CellAt(index);
        public HackingHit? HitAt(int index) => inner.HitAt(index);

        public HackingGuess Select(HackingHit hit)
        {
            HackingGuess guess = inner.Select(hit);
            if (guess.Kind == HackingGuessKind.Correct)
                record(HackingOutcome.Accepted);
            else if (guess.Kind == HackingGuessKind.LockedOut)
                record(HackingOutcome.LockedOut);
            return guess;
        }
    }

    private RuleVerdict Verdict(int count, IReadOnlyList<RecipeCondition>? rows) =>
        count == 0 ? RuleVerdict.True
        : _conditions is null || rows is null || rows.Count != count ? RuleVerdict.Unevaluable
        : _conditions.Evaluate(rows.Select(RecordCondition.From).ToArray());

    private static TerminalEntry Entry(int index, TerminalMenuEntry item, bool passes, ITagResolver? tags)
    {
        string id = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string text = TagExpander.Expand(Clean(item.ItemText.Text), tags);
        string? response = Clean(item.ResponseText.Text) is { Length: > 0 } r ? r : null;
        string? unavailable = passes ? null : string.Empty;
        return item.Type switch
        {
            TerminalItemType.Submenu => new TerminalEntry(id, text, TerminalEntryKind.Submenu, response,
                item.Submenu is { } key ? RecordKey.Format(key) : null, Unavailable: unavailable),
            TerminalItemType.ReturnToTop => new TerminalEntry(id, text, TerminalEntryKind.ReturnToTop, response, Unavailable: unavailable),
            TerminalItemType.ForceRedraw => new TerminalEntry(id, text, TerminalEntryKind.Redraw, response, Unavailable: unavailable),
            TerminalItemType.DisplayText => new TerminalEntry(id, text, TerminalEntryKind.Text, response,
                PageText: Clean(item.DisplayText.Text), Unavailable: unavailable),
            TerminalItemType.DisplayImage => new TerminalEntry(id, text, TerminalEntryKind.Image, response, Unavailable: unavailable),
            _ => new TerminalEntry(id, text, TerminalEntryKind.Unsupported, response, Unavailable: unavailable),
        };
    }

    private static string Clean(string? text) => text?.Replace("\r", string.Empty) ?? string.Empty;
}
