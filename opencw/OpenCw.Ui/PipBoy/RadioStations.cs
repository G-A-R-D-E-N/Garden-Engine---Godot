using System;
using System.Collections.Generic;
using System.Globalization;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public static class RadioStations
{
    public const string FrequencyPath = "REFR\\RadioFrequency";

    // Invariant: a station is a quest alias that forces a transmitter reference with a radio frequency and a named MESG; the first quest to name a transmitter wins.
    public static IReadOnlyList<RadioStation> Build(IRecordReader records, IEnumerable<string> questIds)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(questIds);
        var stations = new Dictionary<string, RadioStation>(StringComparer.Ordinal);
        foreach (var quest in questIds)
        {
            foreach (var alias in records.AliasDisplays(quest))
            {
                if (alias.Reference is not { } transmitter || stations.ContainsKey(transmitter))
                    continue;
                if (!double.TryParse(records.Scalar(transmitter, FrequencyPath), NumberStyles.Float, CultureInfo.InvariantCulture, out var frequency)
                    || !double.IsFinite(frequency))
                    continue;
                if (records.Name(alias.Display) is not { Length: > 0 } name)
                    continue;
                stations[transmitter] = new RadioStation(transmitter, name, Frequency: frequency);
            }
        }
        return [.. stations.Values];
    }
}
