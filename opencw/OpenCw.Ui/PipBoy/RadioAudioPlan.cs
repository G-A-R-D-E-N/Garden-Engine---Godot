using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public enum RadioAudioAction
{
    None,
    Start,
    Stop,
    Mute,
    Unmute,
}

public sealed record RadioAudioStep(RadioAudioAction Action, string? Path = null);

public static class RadioAudioPlan
{
    public const string Folder = "Music/Radio/";

    public static string? PathFor(RadioStation station)
    {
        var name = new string(station.Name.Where(char.IsLetterOrDigit).ToArray());
        return name.Length == 0 ? null : $"{Folder}{name}.xwm";
    }

    public static RadioAudioStep Step(RadioEvent command, IEnumerable<RadioStation> stations)
    {
        switch (command.Kind)
        {
            case RadioEventKind.Stop: return new RadioAudioStep(RadioAudioAction.Stop);
            case RadioEventKind.Mute: return new RadioAudioStep(RadioAudioAction.Mute);
            case RadioEventKind.Unmute: return new RadioAudioStep(RadioAudioAction.Unmute);
            case RadioEventKind.Play when command.StationId is { } id && !id.StartsWith(RadioModel.TapePrefix, System.StringComparison.Ordinal):
                var station = stations.FirstOrDefault(s => s.Id == id);
                return station is { Enabled: true } && PathFor(station) is { } path ? new RadioAudioStep(RadioAudioAction.Start, path) : new RadioAudioStep(RadioAudioAction.None);
            default: return new RadioAudioStep(RadioAudioAction.None);
        }
    }
}
