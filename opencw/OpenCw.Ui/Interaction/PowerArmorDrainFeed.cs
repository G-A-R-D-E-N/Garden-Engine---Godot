namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread; Tick once per player update; action points spent this frame are drained on the next tick.
public sealed class PowerArmorDrainFeed(PowerArmorSession session)
{
    private double? _lastActionPoints;

    public void Tick(double dt, bool sprinting, double actionPoints)
    {
        if (sprinting) session.Drain(PowerArmorDrainKind.Running, dt);
        if (_lastActionPoints is { } last && last > actionPoints) session.Drain(PowerArmorDrainKind.ActionPoint, last - actionPoints);
        _lastActionPoints = actionPoints;
    }
}
