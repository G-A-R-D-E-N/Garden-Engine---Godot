using OpenCommonwealth.Services.Combat;

namespace OpenCommonwealth.Services.Ui.Hud;

// Invariant: the Health element is enabled once, on the first frame vitals exist, so a later user choice to hide it is kept.
public sealed class HudVitalsFeed
{
    private bool _attached;

    public void Reset() => _attached = false;

    public void Push(HudState state, PlayerVitals? vitals)
    {
        if (vitals is null)
        {
            _attached = false;
            return;
        }
        if (!_attached)
        {
            _attached = true;
            state.Settings.SetEnabled(HudElement.Health, true);
        }
        state.SetHealth(vitals.Health, vitals.MaxHealth);
    }
}
