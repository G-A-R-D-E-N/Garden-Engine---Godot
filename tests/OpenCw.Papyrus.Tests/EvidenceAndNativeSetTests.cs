using System;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class EvidenceAndNativeSetTests : HostFixture
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
        (EvidenceKeys.Assignment, "PROVEN"),
        (EvidenceKeys.Arithmetic, "PROVEN"),
        (EvidenceKeys.Types, "PROVEN"),
        (EvidenceKeys.Dispatch, "PROVEN"),
        (EvidenceKeys.Arrays, "PROVEN"),
        (EvidenceKeys.Structs, "PROVEN"),
        (EvidenceKeys.Waits, "UNPROVEN"),
    };

    private static readonly (string Key, string Status)[] NativeRows =
    {
        (NativeSet.TraceKey, "ENGINE_POLICY"),
        (NativeSet.NotificationKey, "ENGINE_POLICY"),
        (NativeSet.MessageBoxKey, "ENGINE_POLICY"),
    };

    private static EvidenceRegistry Registry(params (string Key, string Status)[] rows)
    {
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}");
        return EvidenceRegistry.Load($"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}");
    }

    private static EvidenceGate Synthetic() => new(Registry(PolicyRows));

    private static EvidenceGate NativeGate() => TestHosts.Gate(NativeRows);

    [Fact]
    public void TheRecordedRegistryGatesTheEvidenceRows()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json");
        var registry = EvidenceRegistry.Load(File.ReadAllText(path));

        Assert.Equal(EvidenceStatus.Unproven, registry.StatusFor(EvidenceKeys.Assignment));
        Assert.Equal(EvidenceStatus.Unproven, registry.StatusFor(EvidenceKeys.Arithmetic));
        Assert.Equal(EvidenceStatus.EnginePolicy, registry.StatusFor(EvidenceKeys.Frames));
        Assert.Equal(EvidenceStatus.EnginePolicy, registry.StatusFor(EvidenceKeys.OnInitBarrier));
        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor(EvidenceKeys.EventDuringOnInit));
        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor(EvidenceKeys.Waits));
    }

    [Fact]
    public void TheRecordedRegistryCountsWaitAsApproximated()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var registry = EvidenceRegistry.Load(File.ReadAllText(
            Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json")));

        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor(EvidenceKeys.Waits));
    }

    [Fact]
    public void TheRegistryRejectsAForeignSchema()
    {
        Assert.Throws<FormatException>(() => EvidenceRegistry.Load("{\"schema\":\"other\",\"rows\":[{\"key\":\"a\",\"status\":\"PROVEN\"}]}"));
        Assert.Throws<FormatException>(() => EvidenceRegistry.Load("{\"schema\":\"garden.papyrus.evidence-registry\",\"rows\":[{\"key\":\"a\",\"status\":\"MAYBE\"}]}"));
    }

    [Fact]
    public void TheGateAllowsProvenAndPolicyAndCountsApproximations()
    {
        var gate = Synthetic();

        Assert.Equal(GateOutcome.Allowed, gate.Gate(EvidenceKeys.Frames).Outcome);
        Assert.Equal(GateOutcome.Allowed, gate.Gate(EvidenceKeys.Assignment).Outcome);
        Assert.Equal(GateOutcome.Approximated, gate.Gate(EvidenceKeys.EventDuringOnInit).Outcome);
        Assert.Equal(1, gate.ApproximationCount);
        Assert.Equal(0, gate.StopCount);
    }


    [Fact]
    public void BaseNativesAreBoundAndGated()
    {
        var gate = NativeGate();
        var natives = new NativeSet(gate);
        natives.BindBaseSet();

        var host = Track(TestHosts.Create(HostKind.Radicle, gate,
            TestHosts.Stub("Debug", null,
                "Function Trace(String asMessage) global native",
                "Function Notification(String asMessage) global native",
                "Function MessageBox(String asMessage) global native"),
            TestHosts.Stub("Caller", null, "Function Run()\n  Debug.Trace(\"hello\")\nEndFunction\n")));
        natives.Install(host);
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, "Run");

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(3, natives.Count);
        Assert.Equal(new[] { "hello" }, natives.Traces);
        Assert.Equal(new NativeId("Debug", "MessageBox"), natives.Descriptors[0].Native);
        Assert.All(natives.Descriptors, descriptor => Assert.False(string.IsNullOrWhiteSpace(descriptor.EvidenceKey)));
    }

    [Fact]
    public void EveryNativeMustDeclareItsEvidenceKey()
    {
        var natives = new NativeSet(NativeGate());

        Assert.Throws<ArgumentException>(() =>
            natives.Bind(NativeSet.DebugTrace, NativeTargetKind.Static, "",
                _ => PapyrusValue.None));
        Assert.Equal(0, natives.Count);
    }

    [Fact]
    public void AnUnboundNativeIsRefusedAndCounted()
    {
        var natives = new NativeSet(NativeGate());
        natives.BindBaseSet();

        Assert.Throws<InvalidOperationException>(() => natives.Invoke(new NativeId("Game", "GetPlayer"), Array.Empty<PapyrusValue>()));
        Assert.Equal(1, natives.Refusals);
    }

    [Fact]
    public void ValuesKeepTheirKindAndRenderInvariantly()
    {
        Assert.Equal(PapyrusValueKind.None, PapyrusValue.None.Kind);
        Assert.Equal("7", PapyrusValue.FromInt(7).ToString());
        Assert.Equal("1.5", PapyrusValue.FromFloat(1.5).ToString());
        Assert.Equal("True", PapyrusValue.FromBool(true).ToString());
        Assert.Equal("text", PapyrusValue.FromString("text").ToString());
    }

}
