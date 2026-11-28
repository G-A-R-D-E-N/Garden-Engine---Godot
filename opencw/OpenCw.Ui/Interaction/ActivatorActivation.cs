using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Interaction;

public sealed record ActivatorOpener(string Name, Func<ActivationTarget, bool> Open);

// Requires: main thread only. Openers are tried in registration order; the first that opens wins.
public sealed class ActivatorActivation(IReadOnlyList<ActivatorOpener> openers) : IActivationHandler
{
    public bool Handles(ActivationTarget target) => target.BaseType == "Activator";

    public ActivationResult Activate(ActivationTarget target)
    {
        foreach (var opener in openers)
            if (opener.Open(target)) return new(ActivationStatus.Handled, opener.Name);
        return new(ActivationStatus.Handled, "no default action");
    }
}
