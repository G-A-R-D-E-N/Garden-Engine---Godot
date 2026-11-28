using OpenCommonwealth.Services.Ui.Hud;

namespace OpenCommonwealth.Services.Ui.Vats;

public static class VatsHudFeed
{
    // Requires: called after the menu opened; VatsMenuModel.Close raises Closed and then Changed, so the feed ignores changes once the phase is Closed.
    public static void Attach(VatsMenuModel model, HudInstruments instruments)
    {
        if (model.Phase == VatsPhase.Closed)
            return;
        void Feed()
        {
            if (model.Phase == VatsPhase.Closed)
                return;
            instruments.SetActionPointPreview(model.ActionPointPreview, null);
            instruments.SetCritical(model.Snapshot.Critical, model.Snapshot.CriticalReady ? 1 : 0);
        }
        model.Changed += Feed;
        model.Closed += _ =>
        {
            instruments.SetActionPointPreview(null, null);
            instruments.ClearCritical();
        };
        Feed();
    }
}
