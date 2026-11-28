using System;
using System.Collections.Generic;
using System.Text;
using InteropCallbacks = Radicle.Interop.PapyrusHostCallbacks;
using InteropCompiler = Radicle.Interop.PapyrusCompiler;
using InteropOwner = Radicle.Interop.PapyrusOwner;
using InteropRemoteTarget = Radicle.Interop.PapyrusRemoteTarget;
using InteropRuntime = Radicle.Interop.PapyrusRuntime;
using RNative = Radicle.Interop.Native;
using RadicleException = Radicle.Interop.RadicleException;
using Xunit;
using PapyrusClock = Radicle.Interop.Native.PapyrusClock;
using PapyrusLifecycle = Radicle.Interop.Native.PapyrusLifecycle;
using PapyrusOwnerKind = Radicle.Interop.Native.PapyrusOwnerKind;
using PapyrusOverrideOutcome = Radicle.Interop.Native.PapyrusOverrideOutcome;
using PapyrusStatus = Radicle.Interop.Native.PapyrusStatus;

namespace OpenCw.Papyrus.Tests;

// Invariant: the runtime wrapper keeps callbacks rooted, marshals Papyrus values faithfully and keeps parsed pex handles alive until the runtime is disposed; the scripts are compiled by Radicle so the real interpreter runs them.
public class RadiclePapyrusRuntimeTests
{
    private const string Source =
        "Scriptname Probe\nInt total\nInt Property Count Auto\n" +
        "Int Function Hit() global native\nInt Function Wait() global native\n" +
        "Function Ping()\n  total = Hit()\nEndFunction\n" +
        "Function Run()\n  total = Wait()\nEndFunction\n";

    private static readonly InteropOwner Actor = new(PapyrusOwnerKind.Form, "000800:Probe.esp");
    private static readonly InteropOwner Other = new(PapyrusOwnerKind.Form, "000900:Probe.esp");
    private static readonly InteropOwner Quest = new(PapyrusOwnerKind.Form, "000801:Quests.esp");

    private static byte[] CompileProbe()
    {
        using var pex = InteropCompiler.CompileSource(Encoding.UTF8.GetBytes(Source), "Probe.psc").Pex
            ?? throw new InvalidOperationException("Probe.psc did not compile");
        return pex.ToBytes();
    }

    private static InteropRuntime Create(InteropCallbacks? host = null)
    {
        var runtime = new InteropRuntime(host);
        try { runtime.AddPex(CompileProbe()); }
        catch
        {
            runtime.Dispose();
            throw;
        }
        return runtime;
    }

    [Fact]
    public void AttachWithPropertiesStoresTheValueAndReadsItBackWithoutCase()
    {
        using var runtime = Create();
        var instance = runtime.AttachWithProperties("Probe", Actor, new Dictionary<string, object?> { ["Count"] = 5 });
        Assert.True(runtime.TryGetProperty(instance, "COUNT", out var count));
        Assert.Equal(5, count);
        Assert.False(runtime.TryGetProperty(instance, "Missing", out _));
        Assert.Throws<RadicleException>(() => runtime.AttachWithProperties("Probe", Other,
            new Dictionary<string, object?> { ["Count"] = 1, ["COUNT"] = 2 }));
        Assert.Equal(PapyrusLifecycle.NotInitialized, runtime.Lifecycle(instance));
    }

    [Fact]
    public void AnEventRunsTheManagedNativeCallbackAndStoresItsResult()
    {
        var calls = new List<string>();
        using var runtime = Create(new InteropCallbacks(CallNative: (string script, string function, IReadOnlyList<object?> args, out object? result) =>
        {
            calls.Add(function);
            result = 42;
            return true;
        }));
        runtime.RegisterNative("Probe", "Hit");
        var instance = runtime.Attach("Probe", Actor);
        Assert.Equal(PapyrusStatus.Ok, runtime.Initialize(instance));
        Assert.Equal(PapyrusLifecycle.Ready, runtime.Lifecycle(instance));
        Assert.Equal(PapyrusStatus.Ok, runtime.SendEvent(instance, "Ping"));
        Assert.Equal(1, runtime.Step(512).Completed);
        Assert.Equal(new[] { "Hit" }, calls);
        Assert.True(runtime.TryGetVariable(instance, "total", out var total));
        Assert.Equal(42, total);
        runtime.SetVariable(instance, "total", 7);
        Assert.True(runtime.TryGetVariable(instance, "TOTAL", out total));
        Assert.Equal(7, total);
    }

    [Fact]
    public void ALatentNativeParksTheRunUntilItIsCompletedOnce()
    {
        using var runtime = Create(new InteropCallbacks(BeginLatent: (_, _, _) => 9));
        runtime.RegisterNative("Probe", "Wait", latent: true);
        var instance = runtime.Attach("Probe", Actor);
        runtime.Initialize(instance);
        runtime.SendEvent(instance, "Run");
        Assert.Equal(1, runtime.Step(512).Waiting);
        Assert.True(runtime.CompleteLatent(9, 3));
        Assert.Equal(1, runtime.Step(512).Completed);
        Assert.True(runtime.TryGetVariable(instance, "total", out var total));
        Assert.Equal(3, total);
        Assert.False(runtime.CompleteLatent(9, 4));
    }

    [Fact]
    public void ARegisteredOverrideRunsInsteadOfTheScriptBody()
    {
        var seen = new List<(ulong Instance, string Function)>();
        var natives = 0;
        using var runtime = Create(new InteropCallbacks(
            CallNative: (string script, string function, IReadOnlyList<object?> args, out object? result) =>
            {
                natives++;
                result = 1;
                return true;
            },
            Override: (ulong instance, string function, IReadOnlyList<object?> args, out PapyrusOverrideOutcome outcome, out object? result) =>
            {
                seen.Add((instance, function));
                outcome = PapyrusOverrideOutcome.Handled;
                result = 7;
                return true;
            }));
        runtime.RegisterNative("Probe", "Hit");
        runtime.RegisterOverride("Probe", "Ping");
        var instance = runtime.Attach("Probe", Actor);
        runtime.Initialize(instance);
        runtime.SendEvent(instance, "Ping");
        runtime.Step(512);
        Assert.Equal(new[] { (instance, "Ping") }, seen);
        Assert.Equal(0, natives);
    }

    [Fact]
    public void RemoteEventsMatchEveryRegisteredTargetInHandleOrderUntilUnregisteredOrDetached()
    {
        using var runtime = Create();
        var first = runtime.Attach("Probe", Actor);
        var second = runtime.Attach("Probe", Other);
        var firstHandle = runtime.RegisterRemoteEvent(first, Quest, "Quest.OnStageSet");
        var secondHandle = runtime.RegisterRemoteEvent(second, Quest, "quest.onstageset");

        var matched = runtime.MatchRemoteEvent(Quest, "QUEST.ONSTAGESET");
        Assert.Equal(new[] { new InteropRemoteTarget(firstHandle, first), new InteropRemoteTarget(secondHandle, second) }, matched);

        Assert.True(runtime.Unregister(firstHandle));
        Assert.Equal(new[] { new InteropRemoteTarget(secondHandle, second) }, runtime.MatchRemoteEvent(Quest, "Quest.OnStageSet"));
        Assert.True(runtime.Detach(second));
        Assert.Empty(runtime.MatchRemoteEvent(Quest, "Quest.OnStageSet"));
    }

    [Fact]
    public void ADisposedRuntimeRefusesFurtherCalls()
    {
        var runtime = Create();
        var instance = runtime.Attach("Probe", Actor);
        Assert.Equal(PapyrusStatus.Ok, runtime.Initialize(instance));
        runtime.Dispose();
        Assert.Throws<ObjectDisposedException>(() => runtime.Step(1));
        Assert.Throws<ObjectDisposedException>(() => runtime.AddPex(new byte[] { 0 }));
    }

    [Fact]
    public void InstanceStateIsTheAutoStateName()
    {
        using var pex = InteropCompiler.CompileSource(
            Encoding.UTF8.GetBytes("Scriptname Staged\nAuto State Idle\n    Function Poke()\n    EndFunction\nEndState\n"), "Staged.psc").Pex
            ?? throw new InvalidOperationException("Staged.psc did not compile");
        using var runtime = new InteropRuntime();
        runtime.AddPex(pex.ToBytes());
        var instance = runtime.Attach("Staged", Actor);
        Assert.Equal("Idle", runtime.InstanceState(instance));
    }

    [Fact]
    public void FailureAndInstanceStateReadTheInstanceRecord()
    {
        using var runtime = Create();
        var healthy = runtime.Attach("Probe", Actor);
        Assert.Null(runtime.Failure(healthy));
        Assert.Equal(PapyrusStatus.Ok, runtime.Initialize(healthy));
        Assert.Null(runtime.Failure(healthy));
        Assert.Equal("no such instance", runtime.Failure(healthy + 100));
        Assert.Equal(string.Empty, runtime.InstanceState(healthy + 100));

        runtime.RegisterOverride("Probe", "OnInit");
        var refused = runtime.Attach("Probe", Other);
        Assert.Equal(PapyrusStatus.HostError, runtime.Initialize(refused));
        Assert.Equal(PapyrusLifecycle.Failed, runtime.Lifecycle(refused));
        Assert.Equal("the script does not define OnInit", runtime.Failure(refused));
    }

    [Fact]
    public void TheGateRefusesInitializeUntilItAllows()
    {
        var keys = new List<string>();
        var allow = false;
        using var runtime = Create(new InteropCallbacks(Gate: key =>
        {
            keys.Add(key);
            return allow;
        }));
        var instance = runtime.Attach("Probe", Actor);
        Assert.Equal(PapyrusStatus.GateRefused, runtime.Initialize(instance));
        Assert.Equal(PapyrusLifecycle.NotInitialized, runtime.Lifecycle(instance));
        allow = true;
        Assert.Equal(PapyrusStatus.Ok, runtime.Initialize(instance));
        Assert.Equal(PapyrusLifecycle.Ready, runtime.Lifecycle(instance));
        Assert.Equal(new[] { "vm.lifecycle.oninit_barrier", "vm.lifecycle.oninit_barrier" }, keys);
    }

    [Fact]
    public void AThrowingGateSurfacesFromTheInitializeThatRanIt()
    {
        using var runtime = Create(new InteropCallbacks(Gate: key => throw new InvalidOperationException("gate failed")));
        var instance = runtime.Attach("Probe", Actor);
        var thrown = Assert.Throws<InvalidOperationException>(() => runtime.Initialize(instance));
        Assert.Equal("gate failed", thrown.Message);
    }

    [Fact]
    public void AnOverrideThatReturnsFalseLetsTheScriptBodyRun()
    {
        var natives = 0;
        using var runtime = Create(new InteropCallbacks(
            CallNative: (string script, string function, IReadOnlyList<object?> args, out object? result) =>
            {
                natives++;
                result = 1;
                return true;
            },
            Override: (ulong instance, string function, IReadOnlyList<object?> args, out PapyrusOverrideOutcome outcome, out object? result) =>
            {
                outcome = PapyrusOverrideOutcome.Handled;
                result = 99;
                return false;
            }));
        runtime.RegisterNative("Probe", "Hit");
        runtime.RegisterOverride("Probe", "Ping");
        var instance = runtime.Attach("Probe", Actor);
        runtime.Initialize(instance);
        Assert.Equal(PapyrusStatus.Ok, runtime.SendEvent(instance, "Ping"));
        Assert.Equal(1, runtime.Step(512).Completed);
        Assert.Equal(1, natives);
    }

    [Fact]
    public void AThrowingOverrideSurfacesFromTheSendEventThatRanIt()
    {
        using var runtime = Create(new InteropCallbacks(
            Override: (ulong instance, string function, IReadOnlyList<object?> args, out PapyrusOverrideOutcome outcome, out object? result) =>
            {
                throw new InvalidOperationException("override failed");
            }));
        runtime.RegisterOverride("Probe", "Ping");
        var instance = runtime.Attach("Probe", Actor);
        runtime.Initialize(instance);
        var thrown = Assert.Throws<InvalidOperationException>(() => runtime.SendEvent(instance, "Ping"));
        Assert.Equal("override failed", thrown.Message);
    }

    [Fact]
    public void AThrowingNativeHandlerSurfacesFromTheStepThatRanIt()
    {
        using var runtime = Create(new InteropCallbacks(
            CallNative: (string script, string function, IReadOnlyList<object?> args, out object? result) =>
            {
                throw new InvalidOperationException("native failed");
            }));
        runtime.RegisterNative("Probe", "Hit");
        var instance = runtime.Attach("Probe", Actor);
        runtime.Initialize(instance);
        runtime.SendEvent(instance, "Ping");
        var thrown = Assert.Throws<InvalidOperationException>(() => runtime.Step(512));
        Assert.Equal("native failed", thrown.Message);
    }

    [Fact]
    public void DisposeFromACallbackIsRefusedWhileTheNativeCallIsRunning()
    {
        InteropRuntime? runtime = null;
        runtime = Create(new InteropCallbacks(
            CallNative: (string script, string function, IReadOnlyList<object?> args, out object? result) =>
            {
                result = null;
                runtime!.Dispose();
                return true;
            }));
        try
        {
            runtime.RegisterNative("Probe", "Hit");
            var instance = runtime.Attach("Probe", Actor);
            runtime.Initialize(instance);
            runtime.SendEvent(instance, "Ping");
            Assert.Throws<InvalidOperationException>(() => runtime!.Step(512));
            Assert.True(runtime.Detach(instance));
        }
        finally { runtime.Dispose(); }
    }

    [Fact]
    public void ARealTimeTimerFiresOncePerWholeIntervalAndANonRepeatingTimerRetires()
    {
        using var runtime = Create();
        var instance = runtime.Attach("Probe", Actor);
        var repeating = runtime.RegisterTimer(instance, 1.0, 1, PapyrusClock.RealTime);
        var once = runtime.RegisterTimer(instance, 1.0, 0, PapyrusClock.RealTime);
        Assert.Equal(3, runtime.AdvanceClock("GameSession", 2.5, PapyrusClock.RealTime));
        Assert.Equal(1, runtime.AdvanceClock("GameSession", 1.0, PapyrusClock.RealTime));
        Assert.False(runtime.Unregister(once));
        Assert.True(runtime.Unregister(repeating));
    }

    [Fact]
    public void TheClockRefusesForeignOwnersNegativeTimeAndUnknownInstancesAndSkipsOtherClocks()
    {
        using var runtime = Create();
        var instance = runtime.Attach("Probe", Actor);
        var game = runtime.RegisterTimer(instance, 1.0, 1, PapyrusClock.GameTime);
        Assert.Throws<RadicleException>(() => runtime.AdvanceClock("Other", 1.0, PapyrusClock.RealTime));
        Assert.Throws<RadicleException>(() => runtime.AdvanceClock("GameSession", -1.0, PapyrusClock.RealTime));
        Assert.Equal(0, runtime.AdvanceClock("GameSession", 5.0, PapyrusClock.RealTime));
        Assert.True(runtime.Unregister(game));
        Assert.Throws<RadicleException>(() => runtime.RegisterTimer(instance + 100, 1.0, 1, PapyrusClock.RealTime));
    }
}
