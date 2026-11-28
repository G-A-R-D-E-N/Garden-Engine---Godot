using System;

namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread only; Handles is false until Open is set, so a companion's Talk is never swallowed without a menu; canAsk adds recruitable non-companions, whose Talk route already excludes a sneaking player.
public sealed class CompanionActivation(CompanionRoster roster, Func<ActivationTarget, NpcRoute> routeOf, Func<ActivationTarget, bool>? canAsk = null) : IActivationHandler
{
    public Action<ActivationTarget>? Open { get; set; }

    public bool Handles(ActivationTarget target) =>
        Open is not null && target.Groups.Contains("actors") && (roster.IsCompanion(target.RefKey) || canAsk?.Invoke(target) == true) && routeOf(target) == NpcRoute.Talk;

    public ActivationResult Activate(ActivationTarget target)
    {
        Open?.Invoke(target);
        return new ActivationResult(ActivationStatus.Handled, "companion menu");
    }
}
