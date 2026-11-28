using System;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class NativeSetBindingTests
{
    private static EvidenceGate Gate(params (string Key, string Status)[] rows) =>
        new(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}"))}]}}"));

    private static EvidenceGate PolicyGate() => Gate(
        (NativeSet.TraceKey, "ENGINE_POLICY"), (NativeSet.NotificationKey, "ENGINE_POLICY"), (NativeSet.MessageBoxKey, "ENGINE_POLICY"));

    private static PapyrusValue[] Args(params string[] texts) => texts.Select(PapyrusValue.FromString).ToArray();

    [Fact]
    public void TheBaseSetListsItsDescriptorsInOrdinalNameOrderWithTheirKeys()
    {
        var natives = new NativeSet(PolicyGate());
        natives.BindBaseSet();

        var rows = natives.Descriptors.Select(d => (d.Native.ToString(), d.Kind, d.EvidenceKey)).ToArray();

        Assert.Equal(new[]
        {
            ("Debug.MessageBox", NativeTargetKind.Static, "vm.native.debug_message_box"),
            ("Debug.Notification", NativeTargetKind.Static, "vm.native.debug_notification"),
            ("Debug.Trace", NativeTargetKind.Static, "vm.native.debug_trace"),
        }, rows);
    }

    [Fact]
    public void EachSinkRecordsItsFirstArgumentUnderItsOwnPrefix()
    {
        var natives = new NativeSet(PolicyGate());
        natives.BindBaseSet();

        var results = new[]
        {
            natives.Invoke(NativeSet.DebugTrace, Args("a", "ignored")),
            natives.Invoke(NativeSet.DebugNotification, Args("b")),
            natives.Invoke(NativeSet.DebugMessageBox, new[] { PapyrusValue.FromInt(7) }),
        };

        Assert.Equal(new[] { "a", "notification: b", "messagebox: 7" }, natives.Traces);
        Assert.All(results, result => Assert.Equal(PapyrusValueKind.None, result.Kind));
    }

    [Fact]
    public void ASinkCalledWithNoArgumentsRecordsAnEmptyText()
    {
        var natives = new NativeSet(PolicyGate());
        natives.BindBaseSet();

        natives.Invoke(NativeSet.DebugTrace, Array.Empty<PapyrusValue>());
        natives.Invoke(NativeSet.DebugNotification, Array.Empty<PapyrusValue>());
        natives.Invoke(NativeSet.DebugMessageBox, Array.Empty<PapyrusValue>());

        Assert.Equal(new[] { "", "notification: ", "messagebox: " }, natives.Traces);
    }

    [Fact]
    public void ANativeNameMatchesWithoutRegardToCase()
    {
        var natives = new NativeSet(PolicyGate());
        natives.BindBaseSet();

        natives.Invoke(new NativeId("DEBUG", "trace"), Args("x"));

        Assert.Equal(new[] { "x" }, natives.Traces);
        Assert.Equal(0, natives.Refusals);
    }

    [Fact]
    public void BindingTheSameNameAgainReplacesItsBodyAndKeepsOneEntry()
    {
        var natives = new NativeSet(PolicyGate());
        var id = new NativeId("Lib", "Value");
        natives.Bind(id, NativeTargetKind.Static, NativeSet.TraceKey, _ => PapyrusValue.FromInt(1));
        natives.Bind(new NativeId("LIB", "value"), NativeTargetKind.Static, NativeSet.NotificationKey, _ => PapyrusValue.FromInt(2));

        Assert.Equal(1, natives.Count);
        Assert.Equal(2, natives.Invoke(id, Array.Empty<PapyrusValue>()).Integer);
        Assert.Equal(NativeSet.NotificationKey, Assert.Single(natives.Descriptors).EvidenceKey);
    }

    [Fact]
    public void AnUnboundNameIsRefusedWithItsNameAndCountedEachTime()
    {
        var natives = new NativeSet(PolicyGate());

        var first = Assert.Throws<InvalidOperationException>(() => natives.Invoke(new NativeId("Game", "GetPlayer"), Array.Empty<PapyrusValue>()));
        Assert.Throws<InvalidOperationException>(() => natives.Invoke(new NativeId("Game", "GetPlayer"), Array.Empty<PapyrusValue>()));

        Assert.Equal("the native set does not bind Game.GetPlayer", first.Message);
        Assert.Equal(2, natives.Refusals);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankEvidenceKeyIsRejectedAndNothingIsBound(string key)
    {
        var natives = new NativeSet(PolicyGate());

        var error = Assert.Throws<ArgumentException>(() => natives.Bind(NativeSet.DebugTrace, NativeTargetKind.Static, key, _ => PapyrusValue.None));

        Assert.Equal("evidenceKey", error.ParamName);
        Assert.StartsWith("the native Debug.Trace must declare its evidence key", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, natives.Count);
    }

    [Fact]
    public void AnUnprovenKeyStillBindsAndEveryBindAndInvokeCountsAStop()
    {
        var gate = Gate((NativeSet.TraceKey, "UNPROVEN"));
        var natives = new NativeSet(gate);

        natives.Bind(NativeSet.DebugTrace, NativeTargetKind.Static, NativeSet.TraceKey, _ => PapyrusValue.None);
        Assert.Equal(1, natives.Count);
        Assert.Equal(1, gate.StopCount);

        natives.Invoke(NativeSet.DebugTrace, Array.Empty<PapyrusValue>());
        Assert.Equal(2, gate.StopCount);
        Assert.Equal(2, gate.Stops[NativeSet.TraceKey]);
        Assert.Equal(0, natives.Refusals);
    }
}
