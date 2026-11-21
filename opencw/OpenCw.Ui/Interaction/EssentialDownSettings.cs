using System;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Interaction;

// Requires: both numbers are UNVERIFIED guesses from the setting names (see opencw-interact-activation.md ActorState); the fallbacks are our own defaults, not game values.
public sealed record EssentialDownSettings(float RecoverSeconds = 20f, float RegainFraction = 0.5f)
{
    public const string RecoverSecondsSetting = "fEssentialDeathTime";
    public const string RegainFractionSetting = "fEssentialHealthPercentReGain";

    public static EssentialDownSettings Default { get; } = new();

    public static EssentialDownSettings Read(IRecordReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var d = Default;
        float seconds = PickpocketSettings.Value(reader, RecoverSecondsSetting, d.RecoverSeconds);
        float regain = PickpocketSettings.Value(reader, RegainFractionSetting, d.RegainFraction);
        if (regain > 1f) regain /= 100f;
        return new EssentialDownSettings(
            seconds >= 0f ? seconds : d.RecoverSeconds,
            regain is >= 0f and <= 1f ? regain : d.RegainFraction);
    }
}
