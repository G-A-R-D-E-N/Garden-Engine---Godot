#if RADICLE_HOST_AVAILABLE
using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using RNative = Radicle.Interop.Native;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class RadicleHostAdapterTests
{
    private const ulong EngineBit = 0x8000000000000000UL;

    private static RadicleValueAdapter Adapter(HostObjectTable? table = null) =>
        new(table ?? new HostObjectTable());

    [Fact]
    public void IntegerOutsideRadicleIntRangeIsRefused()
    {
        var adapter = Adapter();

        Assert.Throws<OverflowException>(() => adapter.ToRadicle(PapyrusValue.FromInt((long)int.MaxValue + 1)));
        Assert.Throws<OverflowException>(() => adapter.ToRadicle(PapyrusValue.FromInt((long)int.MinValue - 1)));
        Assert.Equal(int.MinValue, Assert.IsType<int>(adapter.ToRadicle(PapyrusValue.FromInt(int.MinValue))));
        Assert.Equal(int.MaxValue, Assert.IsType<int>(adapter.ToRadicle(PapyrusValue.FromInt(int.MaxValue))));
    }

    [Fact]
    public void FloatNarrowsToSingleAndWidensBack()
    {
        var adapter = Adapter();

        var narrowed = Assert.IsType<float>(adapter.ToRadicle(PapyrusValue.FromFloat(0.1)));
        Assert.Equal((float)0.1, narrowed);

        var widened = adapter.FromRadicle(narrowed);
        Assert.Equal(PapyrusValueKind.Float, widened.Kind);
        Assert.Equal((double)(float)0.1, widened.Real);
    }

    [Fact]
    public void ScalarsRoundTripThroughRadicle()
    {
        var adapter = Adapter();

        Assert.Null(adapter.ToRadicle(PapyrusValue.None));
        Assert.Equal(PapyrusValueKind.None, adapter.FromRadicle(null).Kind);

        Assert.Equal(42, Assert.IsType<int>(adapter.ToRadicle(PapyrusValue.FromInt(42))));
        Assert.Equal(42L, adapter.FromRadicle(42).Integer);

        Assert.Equal(true, Assert.IsType<bool>(adapter.ToRadicle(PapyrusValue.FromBool(true))));
        Assert.True(adapter.FromRadicle(true).Flag);

        Assert.Equal("text", Assert.IsType<string>(adapter.ToRadicle(PapyrusValue.FromString("text"))));
        Assert.Equal("text", adapter.FromRadicle("text").Text);
    }

    [Fact]
    public void EngineObjectRoundTripsThroughBitSixtyThreeHandle()
    {
        var adapter = Adapter();
        var engine = new EngineObjectReference("000123:Quests.esp");

        var raw = Assert.IsType<PapyrusObjectRef>(adapter.ToRadicle(PapyrusValue.FromObject(engine)));
        Assert.Equal(EngineBit | 1UL, raw.Handle);
        Assert.Equal(string.Empty, raw.Type);
        Assert.Equal(string.Empty, raw.State);

        var back = adapter.FromRadicle(raw);
        Assert.Equal(PapyrusValueKind.Object, back.Kind);
        Assert.Same(engine, back.Reference);
    }

    [Fact]
    public void EngineObjectsInternByIdentityNotRecordEquality()
    {
        var adapter = Adapter();
        var withOwner = new EngineObjectReference(PapyrusOwnerId.ForForm("000123:Quests.esp"));
        var bare = new EngineObjectReference(withOwner.Identity);
        var other = new EngineObjectReference("000456:Quests.esp");
        Assert.NotEqual(withOwner, bare);

        var first = Assert.IsType<PapyrusObjectRef>(adapter.ToRadicle(PapyrusValue.FromObject(withOwner)));
        var second = Assert.IsType<PapyrusObjectRef>(adapter.ToRadicle(PapyrusValue.FromObject(bare)));
        var third = Assert.IsType<PapyrusObjectRef>(adapter.ToRadicle(PapyrusValue.FromObject(other)));

        Assert.Equal(first.Handle, second.Handle);
        Assert.Equal(EngineBit | 1UL, first.Handle);
        Assert.NotEqual(first.Handle, third.Handle);
        Assert.Equal(EngineBit | 2UL, third.Handle);
        Assert.Same(withOwner, adapter.FromRadicle(second).Reference);
    }

    [Fact]
    public void UnregisteredHandlesAreRefused()
    {
        var adapter = Adapter();

        Assert.Throws<KeyNotFoundException>(() => adapter.FromRadicle(new PapyrusObjectRef(EngineBit | 9UL, "", "")));
        Assert.Throws<KeyNotFoundException>(() => adapter.FromRadicle(new PapyrusObjectRef(7UL, "", "")));
    }

    [Fact]
    public void UnsupportedTypesAreRefused()
    {
        var adapter = Adapter();

        Assert.Throws<NotSupportedException>(() => adapter.ToRadicle(PapyrusValue.FromObject(new object())));
        Assert.Throws<NotSupportedException>(() => adapter.FromRadicle(1L));
        Assert.Throws<NotSupportedException>(() => adapter.FromRadicle(1.0));
    }

    [Fact]
    public void StatusesMapToVmStopReasons()
    {
        AssertStop(RNative.PapyrusStatus.Ok, VmStopReason.Completed);
        AssertStop(RNative.PapyrusStatus.InstructionBudgetExceeded, VmStopReason.BudgetExhausted);
        AssertStop(RNative.PapyrusStatus.Paused, VmStopReason.Paused);
        AssertStop(RNative.PapyrusStatus.Latent, VmStopReason.Waiting);
        AssertStop(RNative.PapyrusStatus.GateRefused, VmStopReason.GateStopped);
        AssertStop(RNative.PapyrusStatus.CallDepthExceeded, VmStopReason.FrameDepthExceeded);
        AssertStop(RNative.PapyrusStatus.HostError, VmStopReason.NativeFailed);
        AssertStop(RNative.PapyrusStatus.UnknownOpcode, VmStopReason.ModelError);
        AssertStop(RNative.PapyrusStatus.UnsupportedOpcode, VmStopReason.ModelError);
        AssertStop(RNative.PapyrusStatus.FunctionNotFound, VmStopReason.ModelError);
        AssertStop(RNative.PapyrusStatus.InternalError, VmStopReason.ModelError);
    }

    [Fact]
    public void OwnerKindsKeepRadicleOrdinals()
    {
        Assert.Equal((int)PapyrusOwnerKind.Form, (int)RNative.PapyrusOwnerKind.Form);
        Assert.Equal((int)PapyrusOwnerKind.PlacedReference, (int)RNative.PapyrusOwnerKind.PlacedReference);
        Assert.Equal((int)PapyrusOwnerKind.DynamicReference, (int)RNative.PapyrusOwnerKind.DynamicReference);
        Assert.Equal((int)PapyrusOwnerKind.QuestAlias, (int)RNative.PapyrusOwnerKind.QuestAlias);
        Assert.Equal((int)PapyrusOwnerKind.RuntimeInstance, (int)RNative.PapyrusOwnerKind.RuntimeInstance);

        var form = RadicleValueAdapter.OwnerMap(PapyrusOwnerId.ForForm("000123:Fallout4.esm"));
        Assert.Equal(RNative.PapyrusOwnerKind.Form, form.Kind);
        Assert.Equal("000123:Fallout4.esm", form.BaseFormKey);
        Assert.Equal("", form.Discriminator);

        Assert.Equal(RNative.PapyrusOwnerKind.PlacedReference,
            RadicleValueAdapter.OwnerMap(PapyrusOwnerId.ForPlacedReference("000123:Fallout4.esm", "placed")).Kind);
        Assert.Equal(RNative.PapyrusOwnerKind.DynamicReference,
            RadicleValueAdapter.OwnerMap(PapyrusOwnerId.ForDynamicReference("000123:Fallout4.esm", "dynamic")).Kind);
        Assert.Equal(RNative.PapyrusOwnerKind.QuestAlias,
            RadicleValueAdapter.OwnerMap(PapyrusOwnerId.ForQuestAlias("000124:Quests.esp", 2)).Kind);

        var instance = RadicleValueAdapter.OwnerMap(PapyrusOwnerId.ForRuntimeInstance("000125:Quests.esp", 3));
        Assert.Equal(RNative.PapyrusOwnerKind.RuntimeInstance, instance.Kind);
        Assert.Equal("000125:Quests.esp", instance.BaseFormKey);
        Assert.Equal("3", instance.Discriminator);
    }

    private static void AssertStop(RNative.PapyrusStatus status, VmStopReason reason)
    {
        var result = RadicleValueAdapter.StatusMap(status, 7);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(status.ToString(), result.Key);
        Assert.Equal(7, result.Instructions);
        Assert.Equal(PapyrusValueKind.None, result.Value.Kind);
    }
}
#endif
