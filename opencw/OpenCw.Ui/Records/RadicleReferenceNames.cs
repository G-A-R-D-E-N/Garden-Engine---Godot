using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

// Invariant: a placed reference (REFR, ACHR) is named by its base record and any other record by itself; an unreadable record or an empty name is null.
public sealed class RadicleReferenceNames(IRecordSource source)
{
    public string? Name(string key)
    {
        try
        {
            var env = source.Environment;
            if (!RecordKey.TryParse(key, out var plugin, out var local) || env.Winner(new FormKey(plugin, local)) is not { } record)
                return null;
            if (env.Info(record).Signature is "REFR" or "ACHR")
            {
                if (env.PlacedReference(record).Base is not { } baseKey || env.Winner(baseKey) is not { } baseRecord)
                    return null;
                record = baseRecord;
            }
            string? name = source.Strings.ResolveName(record) is { } bytes ? RadicleRecordReader.DecodeName(bytes) : env.Info(record).NameText;
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception ex) when (ex is RadicleException or ArgumentException or InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
    }
}
