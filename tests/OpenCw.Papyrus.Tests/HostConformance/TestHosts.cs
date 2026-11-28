using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Papyrus.Host;

namespace OpenCw.Papyrus.Tests;

public enum HostKind
{
    Radicle,
}

public sealed record ScriptSpec(string Name, string Source);

public static class TestHosts
{
    internal static readonly (string Key, string Status)[] PolicyRows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.Assignment, "ENGINE_POLICY"),
        (EvidenceKeys.Arithmetic, "ENGINE_POLICY"),
        (EvidenceKeys.Types, "ENGINE_POLICY"),
        (EvidenceKeys.Dispatch, "ENGINE_POLICY"),
        (EvidenceKeys.Arrays, "ENGINE_POLICY"),
        (EvidenceKeys.Structs, "ENGINE_POLICY"),
        (EvidenceKeys.States, "ENGINE_POLICY"),
        (EvidenceKeys.Variables, "ENGINE_POLICY"),
        (EvidenceKeys.Waits, "APPROXIMATED"),
        (EvidenceKeys.NativeTarget, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (NativeWait.Key, "APPROXIMATED"),
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY"),
    };

    public static IEnumerable<object[]> HostKinds
    {
        get
        {
            yield return new object[] { HostKind.Radicle };
        }
    }

    public static EvidenceGate DefaultGate() => Build(PolicyRows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal));

    public static EvidenceGate Gate(params (string Key, string Status)[] extra)
    {
        var rows = PolicyRows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var (key, status) in extra)
        {
            rows[key] = status;
        }
        return Build(rows);
    }

    public static EvidenceGate GateDenying(string denied, params string[] extraEnginePolicyKeys)
    {
        var rows = PolicyRows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var key in extraEnginePolicyKeys)
        {
            rows.TryAdd(key, "ENGINE_POLICY");
        }
        rows[denied] = "UNPROVEN";
        return Build(rows);
    }

    private static EvidenceGate Build(IReadOnlyDictionary<string, string> rows)
    {
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    public static ScriptSpec Events(string name, params string[] eventNames) =>
        new(name, $"Scriptname {name}\n" + string.Concat(eventNames.Select(entry => HandlerBody(EventSignature(entry)))));

    public static ScriptSpec Stub(string name, string? extends = null, params string[] members) =>
        new(name, $"Scriptname {name}{(extends is null ? string.Empty : " extends " + extends)}\n" +
            string.Concat(members.Select(member => member + "\n")));

    public static string FailingOnInitSource(string name) =>
        $"Scriptname {name}\n" +
        "Function Boom() global native\n" +
        "Function OnInit()\n" +
        "  Boom()\n" +
        "EndFunction\n";

    public static NativeId BoomNative(string script) => new(script, "Boom");

    public static void RegisterBoom(IScriptHost host, string script) =>
        host.RegisterNative(BoomNative(script), _ => throw new InvalidOperationException("boom"));

    private static string EventSignature(string entry) => entry.Contains('(') ? entry : entry + "()";

    private static string HandlerBody(string signature) =>
        signature[..signature.IndexOf('(')].Contains('.')
            ? $"Event {signature}\nEndEvent\n"
            : $"Function {signature}\nEndFunction\n";

    public static IScriptHost Create(HostKind kind, EvidenceGate gate, params ScriptSpec[] scripts)
    {
        var pex = ConformanceScripts.Compile(scripts.Select(script => (script.Name, script.Source)));
        return kind switch
        {
            HostKind.Radicle => Radicle(pex, gate),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    public static void Dispose(IScriptHost host)
    {
        if (host is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public static int NativeTargetRefusals(IScriptHost host) => host switch
    {
        RadicleScriptHost radicle => radicle.NativeTargetRefusals,
        _ => throw new ArgumentOutOfRangeException(nameof(host), host, null),
    };

    public static int RefusedEvents(IScriptHost host) => host switch
    {
        RadicleScriptHost radicle => radicle.RefusedEvents,
        _ => throw new ArgumentOutOfRangeException(nameof(host), host, null),
    };

    public static (int Ready, int Parked, int WaitingEvents, int RefusedCompletions) Counters(IScriptHost host) => host switch
    {
        RadicleScriptHost radicle => (radicle.Stats.Ready, radicle.Stats.Parked, radicle.Stats.WaitingEvents,
            radicle.RefusedCompletions),
        _ => throw new ArgumentOutOfRangeException(nameof(host), host, null),
    };

    public static string Describe(IScriptHost host, VmResult? result)
    {
        var parts = new List<string>
        {
            result is null
                ? "result=none"
                : $"reason={result.Reason} key={result.Key ?? "-"} message={result.Message ?? "-"} instructions={result.Instructions}",
            $"host={host.GetType().Name} instances={host.Instances.Count}",
        };
        switch (host)
        {
            case RadicleScriptHost radicle:
                parts.Add($"refusedEvents={radicle.RefusedEvents} nativeTargetRefusals={radicle.NativeTargetRefusals}");
                parts.Add($"lastNativeFailure={radicle.LastNativeFailure ?? "-"}");
                parts.Add($"lastRegistrationFailure={radicle.LastRegistrationFailure ?? "-"}");
                parts.Add($"lastFailure={radicle.LastFailureSummary ?? "-"}");
                break;
        }
        return string.Join("; ", parts);
    }

    private static IScriptHost Radicle(IReadOnlyDictionary<string, byte[]> pex, EvidenceGate gate)
    {
        return new RadicleScriptHost(gate, pex.Values);
    }
}
