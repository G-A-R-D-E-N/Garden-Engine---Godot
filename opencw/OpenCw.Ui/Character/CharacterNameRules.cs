using System;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Dialogs;

namespace OpenCommonwealth.Services.Ui.Character;

public enum NameProblem { None, Empty, TooLong, InvalidCharacter }

public readonly record struct NameCheck(string Name, NameProblem Problem, int MaxLength)
{
    public bool IsValid => Problem == NameProblem.None;

    public string? Message => Problem switch
    {
        NameProblem.Empty => "Enter a name",
        NameProblem.TooLong => string.Create(CultureInfo.InvariantCulture, $"Name is too long (at most {MaxLength})"),
        NameProblem.InvalidCharacter => "Name has characters that are not allowed",
        _ => null,
    };
}

public interface ICharacterNameRules
{
    int MaxLength { get; }

    NameCheck Check(string? raw);
}

public sealed class CharacterNameRules : ICharacterNameRules
{
    public static readonly CharacterNameRules Special = new(26, true);

    public static readonly CharacterNameRules NewGame = new(64, false);

    private readonly bool _strip;

    public CharacterNameRules(int maxLength, bool stripUnsafe)
    {
        if (maxLength < 1) throw new ArgumentOutOfRangeException(nameof(maxLength));
        MaxLength = maxLength;
        _strip = stripUnsafe;
    }

    public int MaxLength { get; }

    public NameCheck Check(string? raw)
    {
        var text = raw ?? string.Empty;
        if (_strip) text = new string(text.Where(c => c != '<' && c != '>' && !char.IsControl(c)).ToArray());
        text = text.Trim();
        if (text.Length == 0) return new NameCheck(text, NameProblem.Empty, MaxLength);
        if (!_strip && text.Any(char.IsControl)) return new NameCheck(text, NameProblem.InvalidCharacter, MaxLength);
        return new NameCheck(text, text.Length > MaxLength ? NameProblem.TooLong : NameProblem.None, MaxLength);
    }
}

public sealed class CharacterNameModel
{
    private readonly ICharacterNameRules _rules;
    private TextPromptModel? _editor;

    public CharacterNameModel(string? initial, ICharacterNameRules? rules = null)
    {
        _rules = rules ?? CharacterNameRules.Special;
        Name = _rules.Check(initial).Name;
    }

    public event Action? Changed;

    public string Name { get; private set; }

    public bool IsEditing => _editor is not null;

    public string EditText => _editor?.Value ?? string.Empty;

    public TextPromptModel? Editor => _editor;

    public NameCheck CurrentCheck => _rules.Check(IsEditing ? EditText : Name);

    public bool BeginEdit()
    {
        if (IsEditing) return false;
        _editor = new TextPromptModel(Name, new TextPromptOptions
        {
            MaxLength = _rules.MaxLength,
            Trim = TrimMode.None,
            AllowEmpty = true,
            SelectAllInitially = true,
        });
        _editor.Changed += OnEditorChanged;
        Changed?.Invoke();
        return true;
    }

    public bool EndEdit()
    {
        if (_editor is null) return false;
        _editor.Changed -= OnEditorChanged;
        Name = _rules.Check(_editor.Value).Name;
        _editor = null;
        Changed?.Invoke();
        return true;
    }

    public bool CancelEdit()
    {
        if (_editor is null) return false;
        _editor.Changed -= OnEditorChanged;
        _editor = null;
        Changed?.Invoke();
        return true;
    }

    private void OnEditorChanged() => Changed?.Invoke();
}
