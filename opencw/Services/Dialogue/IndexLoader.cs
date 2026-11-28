using System;
using System.Threading.Tasks;

namespace OpenCommonwealth.Services.Dialogue;

public sealed class IndexLoader<T> where T : class
{
    public const int MaxAttempts = 3;

    private readonly object _gate = new();
    private Task? _task;
    private T? _value;
    private string? _failure;
    private int _failures;
    private int _generation;

    public T? Value
    {
        get { lock (_gate) return _value; }
    }

    public string? Failure
    {
        get { lock (_gate) return _failure; }
    }

    public int Failures
    {
        get { lock (_gate) return _failures; }
    }

    public Task? Current
    {
        get { lock (_gate) return _task; }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _generation++;
            _task = null;
            _value = null;
            _failure = null;
            _failures = 0;
        }
    }

    // Invariant: every state change happens under _gate, because the build finishes on a pool thread while Start assigns _task on the caller's thread.
    public Task Start(Func<T> build, Action<string, int, bool>? failed = null)
    {
        lock (_gate)
        {
            if (_task is not null)
                return _task;
            int generation = _generation;
            return _task = Task.Run(() => Complete(generation, build, failed));
        }
    }

    private void Complete(int generation, Func<T> build, Action<string, int, bool>? failed)
    {
        T? built = null;
        Exception? error = null;
        try { built = build(); }
        catch (Exception ex) { error = ex; }
        string message;
        int attempt;
        bool retry;
        lock (_gate)
        {
            if (generation != _generation)
                return;
            if (error is null)
            {
                _value = built;
                _failure = null;
                return;
            }
            message = _failure = $"{error.GetType().Name}: {error.Message}";
            attempt = ++_failures;
            retry = attempt < MaxAttempts;
            if (retry)
                _task = null;
        }
        failed?.Invoke(message, attempt, retry);
    }
}

public sealed class BuildOnce
{
    private readonly object _gate = new();
    private bool _built;

    public Exception? Failure { get; private set; }

    // Invariant: a build that threw is never retried on this instance; later calls throw InvalidOperationException wrapping the first failure.
    public void Run(Action build)
    {
        lock (_gate)
        {
            if (_built)
                return;
            if (Failure is not null)
                throw new InvalidOperationException("the dialogue topic index failed earlier and is not usable", Failure);
            try
            {
                build();
                _built = true;
            }
            catch (Exception ex)
            {
                Failure = ex;
                throw;
            }
        }
    }
}
