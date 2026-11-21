using System;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

internal sealed class PairRecordSource : IRecordSource
{
    public RadicleEnvironment Environment { get; }

    public StringTables Strings { get; }

    public PairRecordSource(RadicleEnvironment environment, StringTables strings)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(strings);
        Environment = environment;
        Strings = strings;
    }
}
