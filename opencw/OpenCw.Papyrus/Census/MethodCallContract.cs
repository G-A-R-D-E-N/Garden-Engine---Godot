using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Papyrus.Census;

/// <summary>Whether a method call site could be attributed to an owning script.</summary>
public enum MethodTargetState
{
    /// <summary>The receiver's declared type is known, so the site has a target.</summary>
    Resolved,

    /// <summary>The receiver's declared type is not known, so the site is counted and not attributed.</summary>
    ReceiverTypeUnknown,

    /// <summary>The instruction names no function, so there is nothing to attribute.</summary>
    NoCalledFunction,
}

/// <summary>What a method call site resolves to, and why.</summary>
public sealed record MethodTarget(string CalledFunction, string? ReceiverType, MethodTargetState State, string Reason)
{
    public bool Resolved => State == MethodTargetState.Resolved;
}

/// <summary>
/// Names a method call site's target from the called function and the receiver's declared type.
/// </summary>
/// <remarks>
/// A target is named only from a declared type that the script itself carries. A receiver whose
/// declared type is unknown is not guessed at: the site is reported unresolved and counted, so a
/// census never attributes a call to a script the data does not name. Which PEX operand holds the
/// function and the receiver is the lowering layer's business (RadiclePexLowering), not this one's.
/// </remarks>
public static class MethodCallContract
{
    /// <summary>
    /// Names a <c>callmethod</c> site's target from the function operand and the receiver's declared type.
    /// </summary>
    public static MethodTarget Resolve(string? calledFunction, string? declaredReceiverType)
    {
        if (string.IsNullOrWhiteSpace(calledFunction))
        {
            return new MethodTarget(calledFunction ?? string.Empty, null, MethodTargetState.NoCalledFunction,
                "the instruction names no function, so no target is named");
        }
        if (string.IsNullOrWhiteSpace(declaredReceiverType)
            || string.Equals(declaredReceiverType, "none", StringComparison.OrdinalIgnoreCase))
        {
            return new MethodTarget(calledFunction, null, MethodTargetState.ReceiverTypeUnknown,
                $"the receiver's declared type is not known, so {calledFunction} is counted and not attributed");
        }
        return new MethodTarget(calledFunction, declaredReceiverType, MethodTargetState.Resolved,
            $"the receiver's declared type is {declaredReceiverType}");
    }
}
