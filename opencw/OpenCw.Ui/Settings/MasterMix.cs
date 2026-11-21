namespace OpenCommonwealth.Services.Ui.Settings;

public sealed class MasterMix
{
    public const double SilentBelow = 0.001;

    public double Volume { get; set; } = 1;

    public bool MuteUnfocused { get; set; }

    public bool Focused { get; set; } = true;

    public bool Muted => Volume <= SilentBelow || (MuteUnfocused && !Focused);

    public double Db => Volume <= SilentBelow ? SettingsEffects.SilenceDb : SettingsEffects.LinearToDb(Volume);
}
