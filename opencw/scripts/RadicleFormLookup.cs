using System;
using System.Collections.Generic;
using OpenCommonwealth.Services;
using RadicleKey = Radicle.Interop.FormKey;

namespace OpenCommonwealth;

internal static class RadicleFormLookup
{
    // Requires: main thread; the load order check and the read both happen under the EnvLifetime read lock, so a swap must not escape as an exception.
    public static (string Key, string? Signature)? Resolve(string key) => Read<(string, string?)?>(key, (records, parsed) =>
        records.Winner(parsed) is { } record ? (key, records.Info(record).Signature) : null);

    public static double? GlobalValue(string key) => Read(key, (records, parsed) =>
        records.Winner(parsed) is { } record && records.Info(record).Signature == "GLOB"
            ? records.Environment.GlobalValue(record, constantOnly: false).Value
            : (double?)null);

    // Requires: the box shows the body text, or the title when the record has no body; a missing or non-MESG record answers null.
    public static string? MessageText(string key) => Read<string>(key, (records, parsed) =>
    {
        if (records.Winner(parsed) is not { } record || records.Info(record).Signature != "MESG") return null;
        var message = records.Environment.Message(record, records.Strings);
        return string.IsNullOrEmpty(message.Body) ? message.Title : message.Body;
    });

    // Requires: a placed reference answers the keywords of itself and of its base record.
    public static IReadOnlyCollection<string>? Keywords(string key) => Read<IReadOnlyCollection<string>>(key, (records, parsed) =>
    {
        if (records.Winner(parsed) is not { } record) return null;
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyword in records.Keywords(record)) found.Add(keyword.ToString());
        if (records.Info(record).Signature is "REFR" or "ACHR" && records.Environment.PlacedReference(record).Base is { } baseKey
            && records.Winner(baseKey) is { } baseRecord)
            foreach (var keyword in records.Keywords(baseRecord)) found.Add(keyword.ToString());
        return found;
    });

    private static T? Read<T>(string key, Func<RecordSource, RadicleKey, T?> read)
    {
        var locked = false;
        try
        {
            GameLoader.EnvLifetime.EnterReadLock();
            locked = true;
            if (!GameLoader.HasLoadOrder || !GameLoader.TryParseKey(key, out var parsed)) return default;
            return read(GameLoader.Records, parsed);
        }
        catch (Exception ex) when (ex is Radicle.Interop.RadicleException or InvalidOperationException or ObjectDisposedException) { return default; }
        finally
        {
            if (locked) GameLoader.EnvLifetime.ExitReadLock();
        }
    }
}
