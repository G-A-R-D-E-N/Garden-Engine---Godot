using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Save;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed record SchedulerStepResult(int Spent, int Completed, int Parked, int Paused, int Refused, int Dropped,
    int Waiting = 0);

/// <summary>
/// The bounded, fair script scheduler.
/// </summary>
/// <remarks>
/// A step runs ready events in arrival order, each under its fair share of the budget that is left, so
/// the step can never spend more than its budget and one runaway script cannot consume it. An event
/// that exhausts its share is parked and re-queued for the next step, which is what keeps one script
/// from starving the rest of the ready set: the other events still get their turn inside the same
/// step. After a bounded number of slices a parked event is dropped and counted, so a script that
/// never finishes cannot keep the queue alive forever.
/// </remarks>
public sealed class ScriptScheduler
{
    public const int DefaultBudget = 512;
    public const int DefaultMaxParksPerEvent = 8;

    private readonly ScriptHost _host;
    private readonly EvidenceGate _gate;
    private readonly Queue<Entry> _ready = new();
    private readonly Dictionary<long, WaitingEntry> _waiting = new();
    private readonly HashSet<long> _usedTokens = new();

    public ScriptScheduler(ScriptHost host, int budget = DefaultBudget,
        int maxParksPerEvent = DefaultMaxParksPerEvent)
    {
        _host = host;
        _gate = host.Gate;
        Budget = budget;
        MaxParksPerEvent = maxParksPerEvent;
    }

    public int Budget { get; }

    public int MaxParksPerEvent { get; }

    public int ReadyCount => _ready.Count;

    public int CompletedEvents { get; private set; }

    public int ParkedEvents { get; private set; }

    public int PausedEvents { get; private set; }

    public int RefusedEvents { get; private set; }

    public int DroppedEvents { get; private set; }

    public int WaitingCount => _waiting.Count;

    public int WaitingEvents { get; private set; }

    public int RefusedCompletions { get; private set; }

    public string? LastCompletionRefusal { get; private set; }

    public bool CompleteLatent(long token, PapyrusValue value)
    {
        if (!_waiting.TryGetValue(token, out var entry) || entry.Completion is not null)
        {
            RefusedCompletions++;
            LastCompletionRefusal = $"token {token} is unknown or already completed";
            return false;
        }
        _waiting[token] = entry with { Completion = value };
        return true;
    }

    public IReadOnlyList<SavedThread> CaptureWaitingThreads(Func<ScriptInstance, string> ownerKey,
        Func<PapyrusScript, string> fingerprint) => _waiting.OrderBy(entry => entry.Key).Select(entry =>
        {
            var execution = entry.Value.Event.Execution!;
            var frame = execution.Frames[^1];
            return new SavedThread(ownerKey(entry.Value.Event.Instance), frame.Script.Name, fingerprint(frame.Script),
                frame.Function.Name, frame.Pc, frame.Locals.Select(value => value.ToString()).ToList(),
                entry.Key, execution.Waiting!.Native.ToString());
        }).ToList();

    internal VmResult RunEvent(ScriptInstance instance, string function, PapyrusValue[] arguments)
    {
        var execution = _host.PrepareEvent(instance, function, arguments);
        var result = _host.Vm.RunSlice(execution, _host.Vm.Budget);
        return result.Reason == VmStopReason.Waiting
            ? TrackWaiting(new Entry(instance, function, arguments, execution, 0), result) : result;
    }

    private VmResult TrackWaiting(Entry entry, VmResult result)
    {
        var token = entry.Execution!.Waiting!.Token;
        if (!_usedTokens.Add(token))
        {
            RefusedEvents++;
            return result with { Reason = VmStopReason.ModelError, Message = $"latent token {token} was already used" };
        }
        _waiting.Add(token, new WaitingEntry(entry));
        WaitingEvents++;
        return result;
    }

    public void Enqueue(ScriptInstance instance, string function, params PapyrusValue[] arguments) =>
        _ready.Enqueue(new Entry(instance, function, arguments, null, 0));

    public SchedulerStepResult RunStep() => RunStep(Budget);

    /// <summary>Runs one step under an explicit budget, which it can never exceed.</summary>
    public SchedulerStepResult RunStep(int budget)
    {
        var decision = _gate.Gate(EvidenceKeys.SchedulerBudget);
        if (!decision.Allowed)
        {
            var rejected = _ready.Count;
            RefusedEvents += rejected;
            _ready.Clear();
            return new SchedulerStepResult(0, 0, 0, 0, rejected, 0, WaitingCount);
        }

        foreach (var (token, waiting) in _waiting.ToArray())
        {
            if (waiting.Completion is not { } value || waiting.Event.Instance.Detached) continue;
            PapyrusVm.ResumeLatent(waiting.Event.Execution!, value);
            _ready.Enqueue(waiting.Event);
            _waiting.Remove(token);
        }

        var spent = 0;
        var completed = 0;
        var parked = 0;
        var paused = 0;
        var refused = 0;
        var dropped = 0;
        var count = _ready.Count;

        for (var index = 0; index < count && spent < budget; ++index)
        {
            var entry = _ready.Dequeue();
            if (entry.Instance.Detached)
            {
                _ready.Enqueue(entry);
                continue;
            }
            var remainingEvents = count - index;
            var share = Math.Max(1, (budget - spent) / remainingEvents);
            var execution = entry.Execution ?? _host.PrepareEvent(entry.Instance, entry.Function, entry.Arguments);
            var result = _host.Vm.RunSlice(execution, share);
            spent += result.Instructions;

            switch (result.Reason)
            {
                case VmStopReason.Waiting:
                    if (TrackWaiting(entry with { Execution = execution }, result).Reason != VmStopReason.Waiting)
                        refused++;
                    break;
                case VmStopReason.Completed:
                    completed++;
                    CompletedEvents++;
                    break;
                case VmStopReason.Paused:
                    paused++;
                    PausedEvents++;
                    break;
                case VmStopReason.BudgetExhausted:
                    var parks = entry.Parks + 1;
                    if (parks >= MaxParksPerEvent)
                    {
                        dropped++;
                        DroppedEvents++;
                    }
                    else
                    {
                        parked++;
                        ParkedEvents++;
                        _ready.Enqueue(entry with { Execution = execution, Parks = parks });
                    }
                    break;
                default:
                    refused++;
                    RefusedEvents++;
                    break;
            }
        }

        return new SchedulerStepResult(spent, completed, parked, paused, refused, dropped, WaitingCount);
    }

    private sealed record Entry(ScriptInstance Instance, string Function, PapyrusValue[] Arguments,
        PapyrusExecution? Execution, int Parks);

    private sealed record WaitingEntry(Entry Event, PapyrusValue? Completion = null);
}
