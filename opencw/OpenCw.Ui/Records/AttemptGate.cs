namespace OpenCommonwealth.Services.Ui.Records;

// Requires: one owner thread. Allows up to max attempts at a recoverable step; Succeeded closes the gate for good, Reset reopens it (a new load session).
public sealed class AttemptGate(int max = 3)
{
    private int _used;
    private bool _done;

    public bool TryBegin()
    {
        if (_done || _used >= max) return false;
        _used++;
        return true;
    }

    public void Succeeded() => _done = true;

    public void Reset()
    {
        _used = 0;
        _done = false;
    }
}
