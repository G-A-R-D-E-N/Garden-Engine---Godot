namespace OpenCommonwealth.Services.Papyrus.Vm;

public enum VmStopReason
{
    Completed,

    /// <summary>The evidence gate refused the next semantic.</summary>
    GateStopped,

    BudgetExhausted,

    /// <summary>A latent wait parked the execution. Garden approximates it and counts every reliance.</summary>
    Paused,

    /// <summary>The program is malformed, or an operation had no defined result.</summary>
    ModelError,

    NativeFailed,

    /// <summary>The explicit frame stack hit its limit. The C# stack is never used for calls.</summary>
    FrameDepthExceeded,

    Waiting,
}
