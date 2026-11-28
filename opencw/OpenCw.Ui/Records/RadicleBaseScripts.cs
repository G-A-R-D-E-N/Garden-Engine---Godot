using System.Collections.Generic;
using System.Linq;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

// Requires: the caller holds the GameLoader.EnvLifetime read lock; a missing winner, a record with no VMAD or a Radicle error reads as no script names.
public static class RadicleBaseScripts
{
    public static IReadOnlyList<string> Names(RadicleEnvironment environment, FormKey record)
    {
        try { return environment.Winner(record) is { } found && environment.TryScripts(found, out var scripts) ? scripts.Select(script => script.Name).ToList() : []; }
        catch (RadicleException) { return []; }
    }
}
