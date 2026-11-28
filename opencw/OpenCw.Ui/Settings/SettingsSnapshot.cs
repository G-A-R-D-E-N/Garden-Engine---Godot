using System;

namespace OpenCommonwealth.Services.Ui.Settings;

public sealed record SettingsSnapshot
{
    private static readonly string[] Ids =
    {
        "graphics.view_distance",
        "controls.vibration",
        "gameplay.difficulty",
        "gameplay.autosave",
        "gameplay.autosave_interval",
        "gameplay.hints",
    };

    public double ViewDistance { get; init; } = 0.7;

    public bool Vibration { get; init; } = true;

    public string Difficulty { get; init; } = "normal";

    public bool Autosave { get; init; } = true;

    public int AutosaveMinutes { get; init; } = 10;

    public bool TutorialHints { get; init; } = true;

    public static SettingsSnapshot From(Func<string, SettingValue> applied)
    {
        ArgumentNullException.ThrowIfNull(applied);
        var snapshot = new SettingsSnapshot();
        foreach (var id in Ids)
        {
            foreach (var effect in SettingsEffects.Map(id, applied(id)))
            {
                snapshot = effect switch
                {
                    SetViewDistance view => snapshot with { ViewDistance = view.Fraction },
                    SetVibration vibration => snapshot with { Vibration = vibration.Enabled },
                    SetDifficulty difficulty => snapshot with { Difficulty = difficulty.Level },
                    SetAutosave autosave => snapshot with { Autosave = autosave.Enabled },
                    SetAutosaveInterval interval => snapshot with { AutosaveMinutes = interval.Minutes },
                    SetTutorialHints hints => snapshot with { TutorialHints = hints.Enabled },
                    _ => snapshot,
                };
            }
        }
        return snapshot;
    }
}
