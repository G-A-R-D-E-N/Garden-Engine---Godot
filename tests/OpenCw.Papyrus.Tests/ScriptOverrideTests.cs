using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ScriptOverrideTests
{
    private static readonly (string Key, string Status)[] PolicyRows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.NativeTarget, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (EvidenceKeys.Assignment, "UNPROVEN"),
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.ReflectionKey, "ENGINE_POLICY"),
        (NativeSet.TraceKey, "ENGINE_POLICY"),
        (NativeSet.NotificationKey, "ENGINE_POLICY"),
        (NativeSet.MessageBoxKey, "ENGINE_POLICY"),
    };

    private static EvidenceRegistry Registry(params (string Key, string Status)[] rows)
    {
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}");
        return EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}");
    }

    private static EvidenceGate Gate() => new(Registry(PolicyRows));

    private static ScriptLibrary Library(params PapyrusScript[] scripts)
    {
        var library = new ScriptLibrary();
        foreach (var script in scripts)
        {
            library.Add(script);
        }
        return library;
    }

    private static PapyrusScript Script(string name, params PapyrusFunction[] functions) =>
        new(name, functions, Array.Empty<string>());

    private static PapyrusFunction Function(string name, int parameterCount, params PapyrusInstruction[] body) =>
        new(name, body, parameterCount);

    private static PapyrusInstruction Return() => PapyrusInstruction.Return();

    private static ScriptHost Host(params PapyrusScript[] scripts)
    {
        var gate = Gate();
        var host = new ScriptHost(Library(scripts), gate);
        host.Overrides = new ScriptOverrideRegistry(gate);
        return host;
    }

    public sealed class ReflectedTarget
    {
        public int Calls { get; private set; }

        [ScriptOverride("Reflected", "Event")]
        public OverrideResult Handle(OverrideContext context)
        {
            Calls++;
            return OverrideResult.Handled(PapyrusValue.FromString("reflected"));
        }
    }

    public sealed class BadSignatureTarget
    {
        [ScriptOverride("Reflected", "Event")]
        public PapyrusValue Wrong(OverrideContext context) => PapyrusValue.None;
    }

    [Fact]
    public void HandledOverrideReplacesBytecode()
    {
        var host = Host(Script("QuestScript",
            Function("OnInit", 0, Return()),
            Function("Event", 0, PapyrusInstruction.Assign(PapyrusOperand.Local(0), PapyrusOperand.Local(1)))));
        var instance = host.Attach("QuestScript");
        Assert.True(host.Initialize(instance).Completed);

        host.Overrides!.Register("QuestScript", "Event", 0, _ => OverrideResult.Handled(PapyrusValue.FromInt(7)));
        var result = host.RunEvent(instance, "Event");

        Assert.True(result.Completed);
        Assert.Equal(7, result.Value.Integer);
        Assert.Equal(1, host.Overrides.HandledCount);
        Assert.Equal(0, result.Instructions);
    }

    [Fact]
    public void DeclinedOverrideFallsThroughToBytecode()
    {
        var host = Host(Script("QuestScript",
            Function("OnInit", 0, Return()),
            Function("Event", 0, Return())));
        var instance = host.Attach("QuestScript");
        Assert.True(host.Initialize(instance).Completed);

        host.Overrides!.Register("QuestScript", "Event", 0, _ => OverrideResult.Declined("not mine"));
        var result = host.RunEvent(instance, "Event");

        Assert.True(result.Completed);
        Assert.Equal(1, host.Overrides.DeclinedCount);
        Assert.True(result.Instructions > 0);
    }

    [Fact]
    public void ANonexistentEventIsRefused()
    {
        var host = Host(Script("QuestScript", Function("OnInit", 0, Return())));
        var instance = host.Attach("QuestScript");
        Assert.True(host.Initialize(instance).Completed);

        var missing = host.RunEvent(instance, "NotAnEvent");
        Assert.False(missing.Completed);
        Assert.Contains("no event named", missing.Message!, StringComparison.Ordinal);
        Assert.Equal(1, host.RefusedEvents);

        host.Overrides!.Register("QuestScript", "NotAnEvent", 0, _ => OverrideResult.Handled(PapyrusValue.None));
        var overridden = host.RunEvent(instance, "NotAnEvent");
        Assert.False(overridden.Completed);
        Assert.Equal(ScriptOverrideRegistry.Key, overridden.Key);
        Assert.Equal(1, host.Overrides.RefusedCount);
    }

    [Fact]
    public void WrongArgumentCountFailsBeforeTheOverrideRuns()
    {
        var host = Host(Script("QuestScript",
            Function("OnInit", 0, Return()),
            Function("Event", 2, Return())));
        var instance = host.Attach("QuestScript");
        Assert.True(host.Initialize(instance).Completed);

        var ran = false;
        host.Overrides!.Register("QuestScript", "Event", 2, _ =>
        {
            ran = true;
            return OverrideResult.Handled(PapyrusValue.None);
        });

        var result = host.RunEvent(instance, "Event", PapyrusValue.FromInt(1));

        Assert.False(result.Completed);
        Assert.False(ran);
        Assert.Equal(1, host.Overrides.RefusedCount);
        Assert.Equal(1, host.RefusedEvents);
    }

    [Fact]
    public void AMethodReturnBecomesTheCallResult()
    {
        var host = Host(Script("QuestScript",
            Function("OnInit", 0, Return()),
            Function("Event", 0, Return())));
        var instance = host.Attach("QuestScript");
        Assert.True(host.Initialize(instance).Completed);

        host.Overrides!.Register("QuestScript", "Event", 0, _ => OverrideResult.Handled(PapyrusValue.FromString("handled")));
        var result = host.RunEvent(instance, "Event");

        Assert.True(result.Completed);
        Assert.Equal("handled", result.Value.Text);
    }

    [Fact]
    public void ReflectionRegistrationFindsMarkedMethods()
    {
        var host = Host(Script("Reflected",
            Function("OnInit", 0, Return()),
            Function("Event", 0, Return())));
        var instance = host.Attach("Reflected");
        Assert.True(host.Initialize(instance).Completed);

        var target = new ReflectedTarget();
        host.Overrides!.RegisterFromReflection(target);
        var result = host.RunEvent(instance, "Event");

        Assert.True(result.Completed);
        Assert.Equal("reflected", result.Value.Text);
        Assert.Equal(1, target.Calls);
        Assert.Equal(1, host.Overrides.Count);

        Assert.Throws<InvalidOperationException>(() =>
            host.Overrides.RegisterFromReflection(new BadSignatureTarget()));
    }

    [Fact]
    public void ASynchronousOnInitOverrideReleasesTheBarrier()
    {
        var host = Host(Script("QuestScript",
            Function("OnInit", 0, PapyrusInstruction.Wait()),
            Function("Event", 0, Return())));
        var instance = host.Attach("QuestScript");

        host.Overrides!.Register("QuestScript", "OnInit", 0, _ => OverrideResult.Handled(PapyrusValue.None));
        var initialized = host.Initialize(instance);

        Assert.True(initialized.Completed);
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
        Assert.Equal(1, host.Overrides.HandledCount);
        Assert.True(host.RunEvent(instance, "Event").Completed);
    }

    [Fact]
    public void OverridesNeverInterceptNatives()
    {
        var script = Script("Caller",
            Function("OnInit", 0, Return()),
            Function("Event", 0,
                PapyrusInstruction.CallStatic("Debug", "Trace", PapyrusOperand.Local(1), new[] { PapyrusOperand.Local(0) }),
                Return()));
        var gate = Gate();
        var natives = new NativeSet(gate);
        natives.BindBaseSet();
        var host = new ScriptHost(Library(script), gate);
        natives.Install(host);
        host.Overrides = new ScriptOverrideRegistry(gate);
        host.Overrides.Register("Debug", "Trace", ScriptOverrideRegistry.AnyParameterCount,
            _ => OverrideResult.Handled(PapyrusValue.None));

        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        var result = host.RunEvent(instance, "Event", PapyrusValue.FromString("native"));

        Assert.True(result.Completed);
        Assert.Equal(new[] { "native" }, natives.Traces);
        Assert.Equal(0, host.Overrides.HandledCount);
    }
}
