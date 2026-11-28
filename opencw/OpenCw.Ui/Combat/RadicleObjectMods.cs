using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Combat;

// Requires: read is RadicleEnvironment.OmodProperties (single door); a key that is not an OMOD winner, or whose DATA cannot be decoded, reads as null.
public sealed class RadicleObjectMods(Func<FormKey, OmodPropertiesRecord?> read) : IObjectModRecords
{
    public IReadOnlyList<OmodPropertyEntry>? Properties(string modKey)
    {
        if (!RecordKey.TryParse(modKey, out var plugin, out var local)) return null;
        OmodPropertiesRecord? record;
        try
        {
            record = read(new FormKey(plugin, local));
        }
        catch (Exception ex) when (ex is RadicleException or InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
        return record?.Properties;
    }
}
