using System.Text;

namespace OpenCommonwealth.Services.Ui.Dialogs;

public enum TrimMode
{
    None,
    Ends,
    Collapse,
}

public readonly record struct PromptValidity(bool IsValid, string? Message);

public sealed record TextPromptOptions
{
    public int MaxLength { get; init; } = 64;
    public Func<Rune, bool>? AllowedChar { get; init; }
    public TrimMode Trim { get; init; } = TrimMode.Ends;
    public bool AllowEmpty { get; init; }
    public string EmptyMessage { get; init; } = "Enter a value.";
    public Func<string, string?>? Validator { get; init; }
    public bool SelectAllInitially { get; init; }
}

public sealed class TextPromptModel
{
    private readonly TextPromptOptions _options;
    private readonly string _original;
    private string _value;
    private string _composition = "";
    private int _anchor;
    private int _caret;

    public TextPromptModel(string? initial = null, TextPromptOptions? options = null)
    {
        _options = options ?? new TextPromptOptions();
        if (_options.MaxLength < 1)
            throw new ArgumentException("MaxLength must be at least 1.", nameof(options));
        _value = Filter(initial ?? "", _options.MaxLength);
        _original = _value;
        _caret = _value.Length;
        _anchor = _options.SelectAllInitially ? 0 : _caret;
    }

    public event Action? Changed;

    public string Value => _value;
    public string Original => _original;
    public int Caret => _caret;
    public int SelectionStart => Math.Min(_anchor, _caret);
    public int SelectionEnd => Math.Max(_anchor, _caret);
    public int SelectionLength => SelectionEnd - SelectionStart;
    public bool HasSelection => _anchor != _caret;
    public string SelectedText => _value.Substring(SelectionStart, SelectionLength);
    public DialogOutcome Outcome { get; private set; }
    public int MaxLength => _options.MaxLength;

    public string Composition => _composition;
    public bool IsComposing => _composition.Length > 0;

    public string DisplayText => IsComposing
        ? _value[..SelectionStart] + _composition + _value[SelectionEnd..]
        : _value;

    public string Result => Normalize(_value, _options.Trim);

    public PromptValidity Validity
    {
        get
        {
            string result = Result;
            if (result.Length == 0)
                return _options.AllowEmpty ? new PromptValidity(true, null) : new PromptValidity(false, _options.EmptyMessage);
            string? message = _options.Validator?.Invoke(result);
            return message is null ? new PromptValidity(true, null) : new PromptValidity(false, message);
        }
    }

    public bool CanConfirm => Outcome == DialogOutcome.Pending && !IsComposing && Validity.IsValid;

    public static string Normalize(string text, TrimMode mode)
    {
        if (mode == TrimMode.None)
            return text;
        string trimmed = text.Trim();
        if (mode == TrimMode.Ends)
            return trimmed;
        var sb = new StringBuilder(trimmed.Length);
        bool space = false;
        foreach (char c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }
            if (space)
                sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static int RuneIndexToUtf16(string text, int runeIndex)
    {
        int units = 0;
        int runes = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (runes == runeIndex)
                break;
            units += rune.Utf16SequenceLength;
            runes++;
        }
        return units;
    }

    public static int Utf16ToRuneIndex(string text, int utf16Index)
    {
        int units = 0;
        int runes = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (units >= utf16Index)
                break;
            units += rune.Utf16SequenceLength;
            runes++;
        }
        return runes;
    }

    public int Insert(string? text)
    {
        if (Outcome != DialogOutcome.Pending)
            return 0;
        bool wasComposing = IsComposing;
        _composition = "";
        int room = _options.MaxLength - (_value.Length - SelectionLength);
        string filtered = Filter(text ?? "", room);
        if (filtered.Length == 0)
        {
            if (wasComposing)
                Changed?.Invoke();
            return 0;
        }
        int start = SelectionStart;
        _value = _value[..start] + filtered + _value[SelectionEnd..];
        _caret = _anchor = start + filtered.Length;
        Changed?.Invoke();
        return filtered.Length;
    }

    public int Paste(string? text)
    {
        if (text is null)
            return 0;
        var sb = new StringBuilder(text.Length);
        bool pendingBreak = false;
        foreach (char c in text)
        {
            if (c is '\r' or '\n' or '\t')
            {
                pendingBreak = true;
                continue;
            }
            if (pendingBreak)
                sb.Append(' ');
            pendingBreak = false;
            sb.Append(c);
        }
        if (pendingBreak)
            sb.Append(' ');
        return Insert(sb.ToString());
    }

    public bool Backspace(bool byWord = false) => Remove(-1, byWord);

    public bool Delete(bool byWord = false) => Remove(1, byWord);

    private bool Remove(int direction, bool byWord)
    {
        if (Outcome != DialogOutcome.Pending || IsComposing)
            return false;
        int start = SelectionStart;
        int end = SelectionEnd;
        if (start == end)
        {
            int target = byWord ? WordBoundary(_caret, direction) : Step(_caret, direction);
            if (target == _caret)
                return false;
            start = Math.Min(target, _caret);
            end = Math.Max(target, _caret);
        }
        _value = _value[..start] + _value[end..];
        _caret = _anchor = start;
        Changed?.Invoke();
        return true;
    }

    public void MoveCaret(int direction, bool extend = false, bool byWord = false)
    {
        int sign = Math.Sign(direction);
        if (sign == 0)
            return;
        if (!extend && HasSelection)
        {
            _caret = _anchor = sign < 0 ? SelectionStart : SelectionEnd;
            Changed?.Invoke();
            return;
        }
        int target = byWord ? WordBoundary(_caret, sign) : Step(_caret, sign);
        _caret = target;
        if (!extend)
            _anchor = target;
        Changed?.Invoke();
    }

    public void SetCaret(int index, bool extend = false)
    {
        int clamped = Math.Clamp(index, 0, _value.Length);
        if (clamped > 0 && clamped < _value.Length && char.IsLowSurrogate(_value[clamped]) &&
            char.IsHighSurrogate(_value[clamped - 1]))
            clamped--;
        _caret = clamped;
        if (!extend)
            _anchor = clamped;
        Changed?.Invoke();
    }

    public void MoveToStart(bool extend = false) => SetCaret(0, extend);

    public void MoveToEnd(bool extend = false) => SetCaret(_value.Length, extend);

    public void SelectAll()
    {
        _anchor = 0;
        _caret = _value.Length;
        Changed?.Invoke();
    }

    public string ReplaceAll(string? text, int caretUtf16)
    {
        if (Outcome != DialogOutcome.Pending)
            return _value;
        _value = Filter(text ?? "", _options.MaxLength);
        _anchor = _caret = 0;
        SetCaret(caretUtf16);
        return _value;
    }

    public void SetComposition(string? text)
    {
        if (Outcome != DialogOutcome.Pending)
            return;
        _composition = text ?? "";
        Changed?.Invoke();
    }

    public int CommitComposition() => Insert(_composition);

    public void CancelComposition()
    {
        if (_composition.Length == 0)
            return;
        _composition = "";
        Changed?.Invoke();
    }

    public string? Confirm()
    {
        if (!CanConfirm)
            return null;
        Outcome = DialogOutcome.Confirmed;
        Changed?.Invoke();
        return Result;
    }

    public string Cancel()
    {
        if (Outcome != DialogOutcome.Pending)
            return _original;
        _composition = "";
        _value = _original;
        _caret = _anchor = _value.Length;
        Outcome = DialogOutcome.Cancelled;
        Changed?.Invoke();
        return _original;
    }

    private string Filter(string text, int room)
    {
        var sb = new StringBuilder();
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || (_options.AllowedChar is { } allowed && !allowed(rune)))
                continue;
            if (sb.Length + rune.Utf16SequenceLength > room)
                break;
            sb.Append(rune.ToString());
        }
        return sb.ToString();
    }

    private int Step(int position, int direction)
    {
        if (direction < 0)
        {
            if (position <= 0)
                return 0;
            int back = position - 1;
            if (back > 0 && char.IsLowSurrogate(_value[back]) && char.IsHighSurrogate(_value[back - 1]))
                back--;
            return back;
        }
        if (position >= _value.Length)
            return _value.Length;
        int forward = position + 1;
        if (char.IsHighSurrogate(_value[position]) && forward < _value.Length && char.IsLowSurrogate(_value[forward]))
            forward++;
        return forward;
    }

    private int WordBoundary(int position, int direction)
    {
        int i = position;
        if (direction < 0)
        {
            while (i > 0 && char.IsWhiteSpace(_value[i - 1]))
                i--;
            while (i > 0 && !char.IsWhiteSpace(_value[i - 1]))
                i--;
            return i;
        }
        while (i < _value.Length && char.IsWhiteSpace(_value[i]))
            i++;
        while (i < _value.Length && !char.IsWhiteSpace(_value[i]))
            i++;
        return i;
    }
}
