using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.Vm;

/// <summary>One bound native: its owner script, its target kind and the key that gates it.</summary>
public sealed record NativeDescriptor(NativeId Native, NativeTargetKind Kind, string EvidenceKey);

/// <summary>
/// The evidence-backed native set.
/// </summary>
/// <remarks>
/// Every native declares the evidence key that gates it, so a native whose engine behavior is not
/// proven is not bound at all. The base set is Garden's own Debug sinks: a script's call is accepted
/// and reported to the host, and nothing about the engine's UI or timing is claimed. A name the set
/// does not bind is refused and counted rather than silently ignored, and a registration without an
/// evidence key is rejected outright.
/// </remarks>
public sealed class NativeSet
{
    public static readonly NativeId DebugTrace = new("Debug", "Trace");
    public static readonly NativeId DebugNotification = new("Debug", "Notification");
    public static readonly NativeId DebugMessageBox = new("Debug", "MessageBox");

    public const string TraceKey = "vm.native.debug_trace";
    public const string NotificationKey = "vm.native.debug_notification";
    public const string MessageBoxKey = "vm.native.debug_message_box";

    private readonly EvidenceGate _gate;
    private readonly Dictionary<NativeId, Entry> _natives = new();
    private readonly List<string> _traces = new();

    public NativeSet(EvidenceGate gate) => _gate = gate;

    public int Count => _natives.Count;

    public int Refusals { get; private set; }

    public IReadOnlyList<string> Traces => _traces;

    public IReadOnlyList<NativeDescriptor> Descriptors =>
        _natives.Values.Select(entry => entry.Descriptor)
            .OrderBy(descriptor => descriptor.Native.ToString(), StringComparer.Ordinal)
            .ToList();

    public void Bind(NativeId name, NativeTargetKind kind, string evidenceKey,
        Func<IReadOnlyList<PapyrusValue>, PapyrusValue> body)
    {
        if (string.IsNullOrWhiteSpace(evidenceKey))
        {
            throw new ArgumentException($"the native {name} must declare its evidence key", nameof(evidenceKey));
        }
        if (!_gate.Gate(evidenceKey).Allowed)
        {
            throw new InvalidOperationException($"the evidence registry does not allow binding {name}");
        }
        _natives[name] = new Entry(new NativeDescriptor(name, kind, evidenceKey), body);
    }

    /// <summary>Binds the base set: the Debug sinks Garden owns.</summary>
    public void BindBaseSet()
    {
        Bind(DebugTrace, NativeTargetKind.Static, TraceKey, arguments =>
        {
            _traces.Add(arguments.Count > 0 ? arguments[0].ToString() : "");
            return PapyrusValue.None;
        });
        Bind(DebugNotification, NativeTargetKind.Static, NotificationKey, arguments =>
        {
            _traces.Add("notification: " + (arguments.Count > 0 ? arguments[0].ToString() : ""));
            return PapyrusValue.None;
        });
        Bind(DebugMessageBox, NativeTargetKind.Static, MessageBoxKey, arguments =>
        {
            _traces.Add("messagebox: " + (arguments.Count > 0 ? arguments[0].ToString() : ""));
            return PapyrusValue.None;
        });
    }

    public void Install(IScriptHost host)
    {
        foreach (var entry in _natives.Values)
        {
            host.RegisterNative(entry.Descriptor.Native, entry.Body, entry.Descriptor.Kind);
        }
    }

    /// <summary>Invokes a bound native, refusing and counting a name the set does not bind.</summary>
    public PapyrusValue Invoke(NativeId name, IReadOnlyList<PapyrusValue> arguments)
    {
        if (!_natives.TryGetValue(name, out var entry))
        {
            Refusals++;
            throw new InvalidOperationException($"the native set does not bind {name}");
        }
        var decision = _gate.Gate(entry.Descriptor.EvidenceKey);
        if (!decision.Allowed)
        {
            Refusals++;
            throw new InvalidOperationException($"{decision.Key}: {decision.Reason}");
        }
        return entry.Body(arguments);
    }

    private sealed record Entry(
        NativeDescriptor Descriptor,
        Func<IReadOnlyList<PapyrusValue>, PapyrusValue> Body);
}
