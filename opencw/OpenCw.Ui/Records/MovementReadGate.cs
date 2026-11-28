using System;

namespace OpenCommonwealth.Services.Ui.Records;

public enum MovementReadStep
{
    Hold,
    Read,
    Defer,
}

public static class MovementReadGate
{
    public const int Unread = -1;

    public static MovementReadStep Decide(bool hasLoadOrder, int cachedSession, int sessionId, bool lockHeld)
    {
        if (!hasLoadOrder || cachedSession == sessionId)
            return MovementReadStep.Hold;
        return lockHeld ? MovementReadStep.Defer : MovementReadStep.Read;
    }

    public static T Resolve<T>(
        ref int cachedSession,
        T current,
        bool hasLoadOrder,
        int sessionId,
        bool lockHeld,
        T builtin,
        Func<T> readFresh)
    {
        switch (Decide(hasLoadOrder, cachedSession, sessionId, lockHeld))
        {
            case MovementReadStep.Defer:
                cachedSession = Unread;
                return builtin;
            case MovementReadStep.Read:
                var fresh = readFresh();
                cachedSession = sessionId;
                return fresh;
            default:
                return current;
        }
    }
}

public sealed class MovementReadSession<T>
{
    public const int MaxRetryCalls = 120;

    private readonly T _builtin;
    private readonly Func<T> _readFresh;
    private readonly Action<string>? _log;
    private int _session = MovementReadGate.Unread;
    private int _failedSession = MovementReadGate.Unread;
    private int _retryIn;
    private int _failures;
    private bool _failureLogged;

    public MovementReadSession(T builtin, Func<T> readFresh, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(readFresh);
        _builtin = builtin;
        _readFresh = readFresh;
        _log = log;
        Set = builtin;
    }

    public T Set { get; private set; }

    public int Session => _session;

    public int RetryIn => _retryIn;

    public T Current(bool hasLoadOrder, int sessionId, bool lockHeld)
    {
        if (hasLoadOrder && sessionId == _failedSession && _retryIn > 0)
        {
            _retryIn--;
            return Set;
        }
        try
        {
            Set = MovementReadGate.Resolve(ref _session, Set, hasLoadOrder, sessionId, lockHeld, _builtin, _readFresh);
            _failureLogged = false;
            _failures = 0;
            _failedSession = MovementReadGate.Unread;
            _retryIn = 0;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _failures = Math.Min(_failures + 1, 10);
            _retryIn = Math.Min(1 << (_failures - 1), MaxRetryCalls);
            _failedSession = sessionId;
            if (!_failureLogged)
            {
                _failureLogged = true;
                _log?.Invoke($"MOVT read failed ({e.GetType().Name}: {e.Message}); using the built-in speeds.");
            }
            Set = _builtin;
        }
        return Set;
    }
}
