using System.Text;

namespace OpenCommonwealth.Services.Ui.Dialogs;

public enum CaseRule
{
    Sensitive,
    Insensitive,
}

public sealed class TypedConfirmModel
{
    private const int ExtraRoom = 16;
    private readonly string _normalizedPhrase;

    public TypedConfirmModel(string phrase, CaseRule caseRule = CaseRule.Sensitive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phrase);
        Phrase = phrase.Trim();
        Case = caseRule;
        _normalizedPhrase = Canonical(Phrase);
        string message = $"Type \"{Phrase}\" to continue.";
        Prompt = new TextPromptModel(null, new TextPromptOptions
        {
            MaxLength = Phrase.Length + ExtraRoom,
            Trim = TrimMode.Collapse,
            EmptyMessage = message,
            Validator = text => IsMatch(text) ? null : message,
        });
    }

    public string Phrase { get; }
    public CaseRule Case { get; }
    public TextPromptModel Prompt { get; }
    public bool Matches => IsMatch(Prompt.Result);
    public DialogOutcome Outcome => Prompt.Outcome;
    public bool CanConfirm => Prompt.CanConfirm;

    public bool Confirm() => Prompt.Confirm() is not null;

    public void Cancel() => Prompt.Cancel();

    private bool IsMatch(string text) => string.Equals(Canonical(text), _normalizedPhrase,
        Case == CaseRule.Insensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Canonical(string text) =>
        TextPromptModel.Normalize(text, TrimMode.Collapse).Normalize(NormalizationForm.FormC);
}
