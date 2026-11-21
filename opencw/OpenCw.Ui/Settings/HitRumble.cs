namespace OpenCommonwealth.Services.Ui.Settings;

public sealed class HitRumble
{
    private double _last;

    public bool Update(double flashAlpha)
    {
        var rose = flashAlpha > _last + 1e-6;
        _last = flashAlpha;
        return rose;
    }
}
