using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class FailedMultiBindTests
{
    private static readonly PapyrusOwnerId Owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");
    private const string Metadata = "[{\"name\":\"First\",\"properties\":[]},{\"name\":\"Second\",\"properties\":[]}]";

    private static readonly string[] Keys =
    {
        "vm.session.cell_lifecycle", "vm.session.activation_routing", "vmad.reference_compiled_attachments",
        "owner.engine_identity", "event.OnActivate", "vmad.reference_source", "vmad.value_binding",
    };

    private static ScriptSpec Handler(string name) => new(name,
        $"Scriptname {name}\nFunction OnInit()\nEndFunction\nFunction OnActivate(ObjectReference akActionRef)\nEndFunction\n");

    private sealed class FlakyHost(IScriptHost inner) : IScriptHost, IDisposable
    {
        public int AttachCalls { get; private set; }
        public int FailAttachCall { get; set; }
        public EvidenceGate Gate => inner.Gate;
        public ScriptOverrideRegistry? Overrides { get => inner.Overrides; set => inner.Overrides = value; }
        public IReadOnlyList<ScriptInstance> Instances => inner.Instances;
        public int WaitingCount => inner.WaitingCount;

        public ScriptInstance Attach(string scriptName, IReadOnlyDictionary<string, PapyrusValue>? properties = null,
            PapyrusOwnerId? owner = null, PapyrusOwnerId? stateIdentity = null)
        {
            if (++AttachCalls == FailAttachCall) throw new InvalidOperationException("synthetic attach failure");
            return inner.Attach(scriptName, properties, owner, stateIdentity);
        }

        public void Discard(ScriptInstance instance) => inner.Discard(instance);
        public VmResult Initialize(ScriptInstance instance) => inner.Initialize(instance);
        public VmResult RunEvent(ScriptInstance instance, string functionName, params PapyrusValue[] arguments) =>
            inner.RunEvent(instance, functionName, arguments);
        public void RegisterNative(NativeId native, Func<IReadOnlyList<PapyrusValue>, PapyrusValue> body,
            NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) =>
            inner.RegisterNative(native, body, kind, requiredScript);
        public void RegisterNative(NativeId native, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> body,
            NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) =>
            inner.RegisterNative(native, body, kind, requiredScript);
        public bool TryRegisterNative(NativeId native, Func<IReadOnlyList<PapyrusValue>, PapyrusValue> body,
            NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) =>
            inner.TryRegisterNative(native, body, kind, requiredScript);
        public bool TryRegisterNative(NativeId native, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> body,
            NativeTargetKind kind = NativeTargetKind.Static, string? requiredScript = null) =>
            inner.TryRegisterNative(native, body, kind, requiredScript);
        public bool HasScript(string name) => inner.HasScript(name);
        public bool HasInvalidParentChain(string name) => inner.HasInvalidParentChain(name);
        public int RunStep(int budget) => inner.RunStep(budget);
        public bool CompleteLatent(long token, PapyrusValue value) => inner.CompleteLatent(token, value);
        public void Dispose() => (inner as IDisposable)?.Dispose();
    }

    [Fact]
    public void AFailedSecondBindRollsBackTheFirstAndARetrySucceeds()
    {
        var gate = TestHosts.Gate(Keys.Select(key => (key, "ENGINE_POLICY")).ToArray());
        var host = new FlakyHost(TestHosts.Create(HostKind.Radicle, gate,
            TestHosts.Stub("ObjectReference"), Handler("First"), Handler("Second"))) { FailAttachCall = 2 };
        using var session = new GameSession(host, gate);

        var failed = session.AttachReference("Played", Owner, Metadata);

        Assert.Equal("synthetic attach failure", failed.Refusal);
        Assert.Empty(failed.Instances);
        Assert.Equal(1, session.RefusedReferences);
        Assert.Equal(0, session.Cells.BoundInstances);
        Assert.Empty(host.Instances);

        var retried = session.AttachReference("Played", Owner, Metadata);

        Assert.Null(retried.Refusal);
        Assert.Equal(2, retried.Instances.Count);
        Assert.Equal(2, session.Cells.BoundInstances);
        Assert.Equal(2, host.Instances.Count);
        Assert.Equal(2, session.Activate(Owner, PapyrusOwnerId.ForPlacedReference("Actor:Probe.esp", "Player:Probe.esp")).Delivered);
    }
}
