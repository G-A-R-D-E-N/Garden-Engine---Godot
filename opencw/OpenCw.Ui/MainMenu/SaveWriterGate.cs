using System;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public sealed class SaveWriterGate : ICanSaveQuery
{
    public const string UnavailableText = "Saving isn't available right now.";

    private readonly Func<Func<string, bool>?> _current;

    public SaveWriterGate(Func<Func<string, bool>?> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _current = current;
    }

    public bool CanSave(out string reason)
    {
        var ready = _current() is not null;
        reason = ready ? string.Empty : UnavailableText;
        return ready;
    }
}
