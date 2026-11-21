using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using InteropObjectRef = Radicle.Interop.PapyrusObjectRef;
using InteropOwner = Radicle.Interop.PapyrusOwner;
using InteropStruct = Radicle.Interop.PapyrusStructValue;
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

    public object? ToRadicle(PapyrusValue value) => ToRadicle(value, null);

    public object? ToRadicle(PapyrusValue value, string? objectType)
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
                return _table.Describe(value.Reference, objectType);
            case PapyrusValueKind.Array:
                var structElement = objectType is { } arrayType && arrayType.Contains('#') && arrayType.EndsWith("[]", StringComparison.Ordinal)
                    ? arrayType[..^2] : null;
                return (value.Elements ?? Array.Empty<PapyrusValue>())
                    .Select(element => ToRadicle(element, element.Kind == PapyrusValueKind.Struct ? structElement : null)).ToList();
            case PapyrusValueKind.Struct:
                var members = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var member in value.Members ?? new Dictionary<string, PapyrusValue>())
                    members[member.Key] = ToRadicle(member.Value, null);
                return new InteropStruct(objectType ?? "", members);
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
        InteropObjectRef reference => PapyrusValue.FromObject(_table.Resolve(reference)),
        IReadOnlyList<object?> items => PapyrusValue.FromArray(items.Select(FromRadicle).ToList()),
        _ => throw new NotSupportedException($"Radicle value type {value?.GetType().Name ?? "null"} has no Papyrus form."),
    };

    public static InteropOwner OwnerMap(PapyrusOwnerId id) =>
        new((RNative.PapyrusOwnerKind)(int)id.Kind, id.BaseFormKey, id.Discriminator);

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

    // Invariant: Radicle's Value::integer is int32; a wider long wraps modulo 2^32 rather than throwing.
    private static int ToInt(long number) => unchecked((int)number);
}
