using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

public interface IRecordSource
{
    RadicleEnvironment Environment { get; }

    StringTables Strings { get; }
}
