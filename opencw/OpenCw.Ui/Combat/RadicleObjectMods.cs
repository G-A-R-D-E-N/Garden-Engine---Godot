using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Combat;

// Requires: read is RadicleEnvironment.OmodProperties (single door); a key that is not an OMOD winner or whose DATA cannot be decoded reads as no rows, and only a failed read answers null so the caller knows to retry.
public sealed class RadicleObjectMods(Func<FormKey, OmodPropertiesRecord?> read) : IObjectModRecords
{
    public IReadOnlyList<ObjectModProperty>? Properties(string modKey)
    {
        if (!RecordKey.TryParse(modKey, out var plugin, out var local)) return Array.Empty<ObjectModProperty>();
        OmodPropertiesRecord? record;
        try
        {
            record = read(new FormKey(plugin, local));
        }
        catch (Exception ex) when (ex is RadicleException or InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
        return record?.Properties.Select(p => new ObjectModProperty(p.ValueType, p.FunctionType, p.Property, p.Value1, p.Value2)).ToList() ?? (IReadOnlyList<ObjectModProperty>)Array.Empty<ObjectModProperty>();
    }
}
