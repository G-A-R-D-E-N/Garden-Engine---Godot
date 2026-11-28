using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Interaction;

public sealed record ActivationTarget(
    string RefKey, string BaseKey, string BaseType, IReadOnlySet<string> Groups,
    int Count = 0, string? Owner = null, bool OwnerNoCrime = false, int OwnerRank = 0, object? Handle = null);

public enum ActivationStatus { Unhandled, Handled, Blocked }

public sealed record ActivationResult(ActivationStatus Status, string Detail = "", bool Reported = false)
{
    public bool NeedsNotice => Status == ActivationStatus.Blocked && !Reported;
}

public interface IActivationHandler
{
    bool Handles(ActivationTarget target);

    ActivationResult Activate(ActivationTarget target);
}

public sealed class DelegateActivationHandler(Func<ActivationTarget, bool> handles, Func<ActivationTarget, ActivationResult> activate)
    : IActivationHandler
{
    public bool Handles(ActivationTarget target) => handles(target);

    public ActivationResult Activate(ActivationTarget target) => activate(target);
}

// Requires: main thread only. Handlers are tried in registration order; the first that accepts the target runs.
public sealed class ActivationDispatcher(Func<ActivationTarget, bool>? deliverScriptEvent = null, Func<string, bool>? isBlocked = null)
{
    private readonly List<IActivationHandler> _handlers = [];
    private readonly HashSet<string> _blocked = new(StringComparer.Ordinal);

    // Requires: set once at start-up by the host, runs on the main thread after a handler ran, and must not throw into activation.
    public static Action<ActivationTarget, ActivationResult>? Dispatched { get; set; }

    public int ScriptEventsDelivered { get; private set; }

    public static ActivationDispatcher Build(Func<ActivationTarget, bool>? deliverScriptEvent, Func<string, bool>? isBlocked, ActivationHandlers handlers)
    {
        var dispatcher = new ActivationDispatcher(deliverScriptEvent, isBlocked);
        foreach (var handler in handlers.InOrder()) dispatcher.Register(handler);
        return dispatcher;
    }

    public void Register(IActivationHandler handler) => _handlers.Add(handler);

    public void Block(string refKey, bool blocked)
    {
        if (blocked) _blocked.Add(refKey);
        else _blocked.Remove(refKey);
    }

    public ActivationResult Dispatch(ActivationTarget target)
    {
        if (deliverScriptEvent?.Invoke(target) == true) ScriptEventsDelivered++;
        if (_blocked.Contains(target.RefKey) || isBlocked?.Invoke(target.RefKey) == true) return new(ActivationStatus.Blocked, "a script blocked the default activation");
        foreach (var handler in _handlers)
            if (handler.Handles(target))
            {
                var result = handler.Activate(target);
                Dispatched?.Invoke(target, result);
                return result;
            }
        return new(ActivationStatus.Unhandled);
    }
}
