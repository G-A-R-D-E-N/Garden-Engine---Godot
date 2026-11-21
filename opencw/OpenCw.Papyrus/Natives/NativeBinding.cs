using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

internal static class NativeBinding
{
    internal readonly record struct Native(
        GameplayNativeFamily Family, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> Handler);

    internal static GameplayNativeFamily Family(string family, string key, string script, string name) =>
        new(family, "Garden.Interaction", key, new NativeId(script, name));

    // Requires: one system key for all natives; a registration conflict keeps the handler that was already registered.
    internal static AdapterReport Bind(IScriptHost host, EvidenceGate gate, string systemKey, string boundReason,
        NativeTargetKind kind, string? requiredScript, params Native[] natives)
    {
        var outcomes = new List<AdapterOutcome>();
        // Requires: this gate call stays; it counts the reliance on the system key.
        gate.Gate(systemKey);
        foreach (var (family, handler) in natives)
        {
            outcomes.Add(host.TryRegisterNative(family.Native, handler, kind, requiredScript)
                ? new AdapterOutcome(family, AdapterState.Bound, boundReason)
                : new AdapterOutcome(family, AdapterState.Conflict, "registration conflict: the existing handler was preserved"));
        }
        var bound = outcomes.FindAll(outcome => outcome.State == AdapterState.Bound).Count;
        return new AdapterReport(outcomes, bound, outcomes.Count - bound);
    }
}
