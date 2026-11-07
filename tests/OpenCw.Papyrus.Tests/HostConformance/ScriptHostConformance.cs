using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public abstract class ScriptHostConformance : IDisposable
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

    private readonly List<IScriptHost> _hosts = new();

    protected abstract IScriptHost Create(IReadOnlyDictionary<string, byte[]> pex, EvidenceGate gate);

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            (host as IDisposable)?.Dispose();
        }
    }

    protected static EvidenceGate Gate()
    {
        var entries = PolicyRows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}");
        var rows = string.Join(",", entries);
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{rows}]}}"));
    }

    protected IScriptHost Open(params (string Name, string Source)[] scripts)
    {
        var host = Create(ConformanceScripts.Compile(scripts), Gate());
        _hosts.Add(host);
        return host;
    }

    private string Describe(VmResult result) =>
        $"{GetType().Name}: {result.Reason} key={result.Key} message={result.Message}";

    private ScriptInstance Ready(IScriptHost host, string script,
        IReadOnlyDictionary<string, PapyrusValue>? properties = null)
    {
        var instance = host.Attach(script, properties);
        var initialized = host.Initialize(instance);
        Assert.True(initialized.Completed, $"OnInit of {script}: {Describe(initialized)}");
        return instance;
    }

    [Fact]
    public void StaticNativeRoundTrip()
    {
        var host = Open(("Probe", ConformanceScripts.Static));
        var received = new List<PapyrusValue>();
        host.RegisterNative(new NativeId("Probe", "Sink"), arguments =>
        {
            received.AddRange(arguments);
            return PapyrusValue.None;
        });

        var result = host.RunEvent(Ready(host, "Probe"), "Run");

        Assert.True(result.Completed, Describe(result));
        Assert.Equal(4, received.Count);
        Assert.Equal(7L, received[0].Integer);
        Assert.Equal(2.5, received[1].Real);
        Assert.True(received[2].Flag);
        Assert.Equal("hi", received[3].Text);
    }

    [Fact]
    public void ScriptInstanceNativeSelf()
    {
        var host = Open(("Probe", ConformanceScripts.Instance));
        ScriptInstance? seen = null;
        host.RegisterNative(new NativeId("Probe", "Poke"), (target, _) =>
        {
            seen = target.Reference as ScriptInstance;
            return PapyrusValue.None;
        }, NativeTargetKind.ScriptInstance);
        var instance = Ready(host, "Probe");

        var run = host.RunEvent(instance, "Run");
        Assert.True(run.Completed, Describe(run));

        Assert.Same(instance, seen);
        Assert.Equal("Probe", seen!.ScriptName);
    }

    [Fact]
    public void OnInitBarrier()
    {
        var host = Open(("Boot", ConformanceScripts.Boot), ("Fragile", ConformanceScripts.Fragile));
        host.RegisterNative(new NativeId("Fragile", "Boom"), _ => throw new InvalidOperationException("boom"));

        var boot = host.Attach("Boot");
        Assert.False(host.RunEvent(boot, "Run").Completed);
        Assert.Equal(ScriptLifecycle.NotInitialized, boot.Lifecycle);
        var bootInit = host.Initialize(boot);
        Assert.True(bootInit.Completed, Describe(bootInit));
        Assert.Equal(ScriptLifecycle.Ready, boot.Lifecycle);
        var bootAgain = host.Initialize(boot);
        Assert.True(bootAgain.Completed, Describe(bootAgain));
        var bootRun = host.RunEvent(boot, "Run");
        Assert.True(bootRun.Completed, Describe(bootRun));

        var fragile = host.Attach("Fragile");
        Assert.False(host.Initialize(fragile).Completed);
        Assert.Equal(ScriptLifecycle.Failed, fragile.Lifecycle);
        Assert.False(host.RunEvent(fragile, "Run").Completed);
    }

    [Fact]
    public void LatentWait()
    {
        var host = Open(("Probe", ConformanceScripts.Latent));
        var received = new List<PapyrusValue>();
        host.RegisterNative(new NativeId("Probe", "Park"), _ => throw new NativeWait(7));
        host.RegisterNative(new NativeId("Probe", "Sink"), arguments =>
        {
            received.AddRange(arguments);
            return PapyrusValue.None;
        });
        var instance = Ready(host, "Probe");

        var parked = host.RunEvent(instance, "Run");

        Assert.Equal(VmStopReason.Waiting, parked.Reason);
        Assert.Equal(1, host.WaitingCount);
        Assert.True(host.CompleteLatent(7, PapyrusValue.FromInt(3)));
        for (var step = 0; step < 10 && host.WaitingCount > 0; ++step)
        {
            host.RunStep(512);
        }
        Assert.Equal(0, host.WaitingCount);
        Assert.Equal(3L, Assert.Single(received).Integer);
        Assert.False(host.CompleteLatent(7, PapyrusValue.FromInt(4)));
    }

    [Fact]
    public void BudgetBounded()
    {
        var host = Open(("Spin", ConformanceScripts.Spin), ("Quick", ConformanceScripts.Quick));
        var spin = Ready(host, "Spin");
        var quick = Ready(host, "Quick");

        Assert.Equal(VmStopReason.BudgetExhausted, host.RunEvent(spin, "Run").Reason);
        var quickRun = host.RunEvent(quick, "Run");
        Assert.True(quickRun.Completed, Describe(quickRun));
    }

    [Fact]
    public void Overrides()
    {
        var host = Open(("Probe", ConformanceScripts.Override));
        var received = new List<PapyrusValue>();
        host.RegisterNative(new NativeId("Probe", "Sink"), arguments =>
        {
            received.AddRange(arguments);
            return PapyrusValue.None;
        });
        var handle = true;
        var registry = new ScriptOverrideRegistry(host.Gate);
        registry.Register("Probe", "Ping", 0, _ => handle
            ? OverrideResult.Handled(PapyrusValue.FromInt(7))
            : OverrideResult.Declined());
        host.Overrides = registry;
        var instance = Ready(host, "Probe");

        var handled = host.RunEvent(instance, "Ping");
        Assert.True(handled.Completed, Describe(handled));
        Assert.Equal(7L, handled.Value.Integer);
        Assert.Empty(received);

        handle = false;
        var pinged = host.RunEvent(instance, "Ping");
        Assert.True(pinged.Completed, Describe(pinged));
        Assert.Equal(1L, Assert.Single(received).Integer);
    }

    [Fact]
    public void HostShape()
    {
        var host = Open(("Probe", ConformanceScripts.Empty));

        Assert.True(host.HasScript("Probe"));
        Assert.False(host.HasScript("Nope"));
        Assert.False(host.HasInvalidParentChain("Probe"));

        var first = host.Attach("Probe");
        var second = host.Attach("Probe");
        var third = host.Attach("Probe");
        Assert.Equal(new[] { first, second, third }, host.Instances);
    }

    [Fact]
    public void AttachWithProperties()
    {
        var received = new List<PapyrusValue>();
        var properties = new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["Count"] = PapyrusValue.FromInt(5),
        };
        IScriptHost host;
        ScriptInstance instance;
        try
        {
            host = Open(("Probe", ConformanceScripts.Property));
            host.RegisterNative(new NativeId("Probe", "Sink"), arguments =>
            {
                received.AddRange(arguments);
                return PapyrusValue.None;
            });
            instance = Ready(host, "Probe", properties);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{GetType().Name}: {ex}", ex);
        }

        var result = host.RunEvent(instance, "Read", PapyrusValue.FromObject(instance));

        Assert.True(result.Completed, Describe(result));
        Assert.Equal(5L, Assert.Single(received).Integer);
    }
}
