using System;

namespace OpenCommonwealth.Services.Papyrus.LiveState;

// Invariant: no Sink means off; callers that build a costly message must test Enabled first; a Sink that throws never reaches the caller (tracing must not change what the engine does) and its latest exception stays in SinkFailure.
public sealed class RuntimeTrace
{
    public Action<string, string>? Sink { get; set; }

    public bool Enabled => Sink is not null;

    public Exception? SinkFailure { get; private set; }

    public void Emit(string category, string message)
    {
        try { Sink?.Invoke(category, message); }
        catch (Exception error) { SinkFailure = error; }
    }
}
