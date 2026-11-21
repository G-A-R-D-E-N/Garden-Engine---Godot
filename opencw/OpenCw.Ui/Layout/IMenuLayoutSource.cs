using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Layout;

public interface IMenuLayoutSource
{
    int AbiVersion { get; }
    string? InstallFingerprint { get; }
    IReadOnlyList<MenuLayout> ReadAll();
}
