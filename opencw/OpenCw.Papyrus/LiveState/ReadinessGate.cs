using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.LiveState;

public enum Subsystem
{
    Actors,
    Vm,
    Quests,
    Nav,
    Audio,
}

public enum ReadyState
{
    NotStarted,
    Pending,
    Ready,
    Failed,
}

public sealed record SubsystemStatus(ReadyState State, Exception? Error, int Failures);

// Invariant: the status is stored before any observer runs and an observer that throws never reaches the reporter (so Fail is safe in a catch block); its exception is kept in ObserverErrors. A failure keeps the exception object it was reported with, never its message, until the subsystem is reported Pending or Ready again; Failures counts every report since the start.
public sealed class ReadinessGate(RuntimeTrace? trace = null)
{
    private static readonly SubsystemStatus Initial = new(ReadyState.NotStarted, null, 0);

    private readonly object _lock = new();
    private readonly Dictionary<Subsystem, SubsystemStatus> _status = new();
    private readonly List<Exception> _observerErrors = new();

    public event Action<Subsystem, SubsystemStatus>? Changed;

    public SubsystemStatus StatusOf(Subsystem subsystem)
    {
        lock (_lock) return _status.GetValueOrDefault(subsystem, Initial);
    }

    public IReadOnlyList<Exception> ObserverErrors
    {
        get { lock (_lock) return _observerErrors.ToArray(); }
    }

    public bool AllReady => Enum.GetValues<Subsystem>().All(subsystem => StatusOf(subsystem).State == ReadyState.Ready);

    public IReadOnlyList<Subsystem> NotReady => Enum.GetValues<Subsystem>().Where(subsystem => StatusOf(subsystem).State != ReadyState.Ready).ToArray();

    public void Pending(Subsystem subsystem) => Set(subsystem, current => new SubsystemStatus(ReadyState.Pending, null, current.Failures));

    public void Ready(Subsystem subsystem) => Set(subsystem, current => new SubsystemStatus(ReadyState.Ready, null, current.Failures));

    public void Fail(Subsystem subsystem, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Set(subsystem, current => new SubsystemStatus(ReadyState.Failed, error, current.Failures + 1));
    }

    public string Describe() => string.Join("; ", Enum.GetValues<Subsystem>().Select(subsystem =>
    {
        var status = StatusOf(subsystem);
        return status.Error is { } error ? $"{subsystem}=Failed ({error.GetType().Name}: {error.Message})" : $"{subsystem}={status.State}";
    }));

    private void Set(Subsystem subsystem, Func<SubsystemStatus, SubsystemStatus> next)
    {
        SubsystemStatus status;
        lock (_lock)
        {
            status = next(_status.GetValueOrDefault(subsystem, Initial));
            _status[subsystem] = status;
        }
        if (trace?.Enabled == true)
            trace.Emit("Readiness", $"{subsystem} -> {status.State}{(status.Error is { } error ? $" ({error.GetType().Name}: {error.Message})" : "")}");
        if (Changed is not { } handlers) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<Subsystem, SubsystemStatus>>())
        {
            try { handler(subsystem, status); }
            catch (Exception error)
            {
                lock (_lock) _observerErrors.Add(error);
            }
        }
    }
}
