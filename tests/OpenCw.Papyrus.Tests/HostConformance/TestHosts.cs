using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Vm;
#if RADICLE_HOST_AVAILABLE
using OpenCommonwealth.Services.Papyrus.Host;
#endif

namespace OpenCw.Papyrus.Tests;

public enum HostKind
{
    CSharp,
    Radicle,
}

public sealed record ScriptSpec(string Name, string Source);

public static class TestHosts
{
    private static readonly (string Key, string Status)[] PolicyRows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.Assignment, "ENGINE_POLICY"),
        (EvidenceKeys.Arithmetic, "ENGINE_POLICY"),
        (EvidenceKeys.Dispatch, "ENGINE_POLICY"),
        (EvidenceKeys.States, "ENGINE_POLICY"),
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
            yield return new object[] { HostKind.CSharp };
#if RADICLE_HOST_AVAILABLE
            yield return new object[] { HostKind.Radicle };
#endif
        }
    }

    public static EvidenceGate DefaultGate()
    {
        var entries = PolicyRows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}");
        var rows = string.Join(",", entries);
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{rows}]}}"));
    }

    public static IScriptHost Create(HostKind kind, EvidenceGate gate, params ScriptSpec[] scripts)
    {
        var pex = ConformanceScripts.Compile(scripts.Select(script => (script.Name, script.Source)));
        return kind switch
        {
            HostKind.CSharp => CSharp(pex, gate),
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
        ScriptHost csharp => csharp.NativeTargetRefusals,
#if RADICLE_HOST_AVAILABLE
        RadicleScriptHost radicle => radicle.NativeTargetRefusals,
#endif
        _ => throw new ArgumentOutOfRangeException(nameof(host), host, null),
    };

    private static IScriptHost CSharp(IReadOnlyDictionary<string, byte[]> pex, EvidenceGate gate)
    {
        var library = new ScriptLibrary();
        foreach (var bytes in pex.Values)
        {
            foreach (var script in RadicleScriptLowering.Lower(bytes))
            {
                library.Add(script);
            }
        }
        return new ScriptHost(library, gate);
    }

    private static IScriptHost Radicle(IReadOnlyDictionary<string, byte[]> pex, EvidenceGate gate)
    {
#if RADICLE_HOST_AVAILABLE
        return new RadicleScriptHost(gate, pex.Values);
#else
        throw new NotSupportedException("the Radicle host is not built into this test assembly");
#endif
    }
}
