using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class EventRegistrations
{
    private readonly List<(ScriptInstance Receiver, string Kind, string Filter)> _entries = new();

    public int Count => _entries.Count(entry => !entry.Receiver.Detached);

    public IReadOnlyList<(ScriptInstance Receiver, string Kind, string Filter)> All =>
        _entries.Where(entry => !entry.Receiver.Detached).ToArray();

    public IReadOnlyList<(ScriptInstance Receiver, string Kind, string Filter)> Of(string kind) =>
        _entries.Where(entry => !entry.Receiver.Detached && entry.Kind == kind).ToArray();

    public void Add(ScriptInstance receiver, string kind, string filter)
    {
        if (!_entries.Contains((receiver, kind, filter))) _entries.Add((receiver, kind, filter));
    }

    public void Remove(ScriptInstance receiver, string kind, string filter) => _entries.Remove((receiver, kind, filter));

    public void Clear() => _entries.Clear();
}

public sealed class EventRegistrationNativeAdapters(IScriptHost host, EvidenceGate gate, EventRegistrations registrations)
{
    public const string SystemKey = "system.event_registrations";

    private static readonly string[] Kinds = { "RemoteEvent", "HitEvent", "MagicEffectApplyEvent", "RadiationDamageEvent", "PlayerSleep", "PlayerTeleport" };

    // Requires: no native delivers these events yet; a registration is only recorded.
    public AdapterReport Bind() => NativeBinding.Bind(host, gate, SystemKey, "registrations are kept; Garden has no source for the event yet",
        NativeTargetKind.ScriptInstance, null,
        Kinds.SelectMany(kind => new[]
        {
            new NativeBinding.Native(NativeBinding.Family("event-registration", SystemKey, "ScriptObject", "RegisterFor" + kind), (receiver, arguments) => Change(receiver, arguments, kind, true)),
            new NativeBinding.Native(NativeBinding.Family("event-registration", SystemKey, "ScriptObject", "UnregisterFor" + kind), (receiver, arguments) => Change(receiver, arguments, kind, false)),
        }).ToArray());

    private PapyrusValue Change(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments, string kind, bool add)
    {
        if (receiver.Reference is not ScriptInstance instance) throw new InvalidOperationException("an event registration needs an attached script as receiver");
        var filter = string.Join("|", arguments.Select(Describe));
        if (add) registrations.Add(instance, kind, filter);
        else registrations.Remove(instance, kind, filter);
        return add && kind == "RemoteEvent" ? PapyrusValue.FromBool(true) : PapyrusValue.None;
    }

    private static string Describe(PapyrusValue value) => value.Kind switch
    {
        PapyrusValueKind.Object => value.Reference switch { EngineObjectReference engine => engine.Identity, ScriptInstance script => script.Owner?.Key ?? script.ScriptName, _ => "?" },
        PapyrusValueKind.None => "none",
        PapyrusValueKind.Int => value.Integer.ToString(),
        PapyrusValueKind.Float => value.Real.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        PapyrusValueKind.Bool => value.Flag ? "true" : "false",
        PapyrusValueKind.String => value.Text ?? "",
        _ => "?",
    };
}
