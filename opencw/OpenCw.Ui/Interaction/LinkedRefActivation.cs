using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Interaction;

public static class LinkedRefActivation
{
    // Requires: main thread; scripted is true for a placement whose own script already ran, activate delivers one activation to the linked reference without going back through ActivationDispatcher.
    public static ActivatorOpener Opener(
        Func<ActivationTarget, bool> scripted, Func<ActivationTarget, IReadOnlyList<string>> linksOf, Func<ActivationTarget, string, bool> activate) =>
        new("linked ref", target =>
        {
            if (scripted(target)) return false;
            var opened = false;
            foreach (var link in linksOf(target).Distinct(StringComparer.OrdinalIgnoreCase))
                if (!string.Equals(link, target.RefKey, StringComparison.OrdinalIgnoreCase)) opened |= activate(target, link);
            return opened;
        });
}
