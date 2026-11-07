using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed class NativeWait : Exception
{
    public const string Key = "vm.native.latent";

    public NativeWait(long token) : base($"waiting on token {token}")
    {
        if (token <= 0) throw new ArgumentOutOfRangeException(nameof(token));
        Token = token;
    }

    public long Token { get; }
}

internal sealed record LatentCall(long Token, NativeId Native, PapyrusOperand ReturnTo);

internal sealed class PapyrusExecution
{
    public List<PapyrusFrame> Frames { get; } = new();
    public VmResult? Result { get; set; }
    public VmStopReason FailureReason { get; set; } = VmStopReason.ModelError;
    public LatentCall? Waiting { get; set; }
}

public sealed partial class PapyrusVm
{
    private string? Native(PapyrusExecution execution, PapyrusFrame frame, PapyrusInstruction instruction,
        NativeId native, NativeTargetKind kind, PapyrusValue target, IReadOnlyList<PapyrusValue> arguments)
    {
        try
        {
            Write(frame, instruction.Result, Natives().Invoke(native, kind, target, arguments));
            return null;
        }
        catch (NativeWait wait)
        {
            var decision = _gate.Gate(NativeWait.Key);
            if (decision.Allowed)
            {
                Write(frame, instruction.Result, PapyrusValue.None);
                execution.Waiting = new LatentCall(wait.Token, native, instruction.Result);
            }
            execution.Result = new VmResult(decision.Allowed ? VmStopReason.Waiting : VmStopReason.GateStopped,
                PapyrusValue.None, decision.Key, decision.Allowed ? $"{native}: {wait.Message}" : decision.Reason, 0);
            return execution.Result.Message;
        }
    }

    internal static void ResumeLatent(PapyrusExecution execution, PapyrusValue value)
    {
        var wait = execution.Waiting ?? throw new InvalidOperationException("the execution is not waiting");
        Write(execution.Frames[^1], wait.ReturnTo, value);
        execution.Waiting = null;
        execution.Result = null;
    }
}
