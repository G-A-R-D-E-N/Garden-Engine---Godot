#if RADICLE_HOST_AVAILABLE
using System;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using RNative = Radicle.Interop.Native;

namespace OpenCommonwealth.Services.Papyrus.Host;

public sealed class RadicleValueAdapter
{
    private readonly HostObjectTable _table;

    public RadicleValueAdapter(HostObjectTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        _table = table;
    }

    public object? ToRadicle(PapyrusValue value)
    {
        switch (value.Kind)
        {
            case PapyrusValueKind.None:
                return null;
            case PapyrusValueKind.Int:
                return ToInt(value.Integer);
            case PapyrusValueKind.Float:
                return (float)value.Real;
            case PapyrusValueKind.Bool:
                return value.Flag;
            case PapyrusValueKind.String:
                return value.Text ?? "";
            case PapyrusValueKind.Object:
                return _table.Describe(value.Reference);
            default:
                throw new NotSupportedException($"Papyrus value kind {value.Kind} has no Radicle form.");
        }
    }

    public PapyrusValue FromRadicle(object? value) => value switch
    {
        null => PapyrusValue.None,
        int number => PapyrusValue.FromInt(number),
        float real => PapyrusValue.FromFloat(real),
        bool flag => PapyrusValue.FromBool(flag),
        string text => PapyrusValue.FromString(text),
        PapyrusObjectRef reference => PapyrusValue.FromObject(_table.Resolve(reference)),
        _ => throw new NotSupportedException($"Radicle value type {value?.GetType().Name ?? "null"} has no Papyrus form."),
    };

    public static PapyrusOwner OwnerMap(PapyrusOwnerId id) =>
        new PapyrusOwner((RNative.PapyrusOwnerKind)(int)id.Kind, id.BaseFormKey, id.Discriminator);

    public static VmResult StatusMap(RNative.PapyrusStatus status, long instructions)
    {
        var reason = status switch
        {
            RNative.PapyrusStatus.Ok => VmStopReason.Completed,
            RNative.PapyrusStatus.InstructionBudgetExceeded => VmStopReason.BudgetExhausted,
            RNative.PapyrusStatus.Paused => VmStopReason.Paused,
            RNative.PapyrusStatus.Latent => VmStopReason.Waiting,
            RNative.PapyrusStatus.GateRefused => VmStopReason.GateStopped,
            RNative.PapyrusStatus.CallDepthExceeded => VmStopReason.FrameDepthExceeded,
            RNative.PapyrusStatus.HostError => VmStopReason.NativeFailed,
            _ => VmStopReason.ModelError,
        };
        return new VmResult(reason, PapyrusValue.None, status.ToString(), null, checked((int)instructions));
    }

    private static int ToInt(long number)
    {
        if (number < int.MinValue || number > int.MaxValue)
        {
            throw new OverflowException($"Papyrus integer {number} does not fit a Radicle int.");
        }
        return (int)number;
    }
}
#endif
