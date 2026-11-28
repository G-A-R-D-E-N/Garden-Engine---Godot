using System.Collections.Generic;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

public static class RadicleKeywords
{
    public static IReadOnlyList<FormKey>? Of(RadicleEnvironment environment, FormKey record)
    {
        try { return environment.Winner(record) is { } found ? environment.RecordKeywords(found) : null; }
        catch (RadicleException) { return null; }
    }
}
