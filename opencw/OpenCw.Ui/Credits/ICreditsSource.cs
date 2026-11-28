using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Credits;

public interface ICreditsSource
{
    IReadOnlyList<string> Lines { get; }
}
