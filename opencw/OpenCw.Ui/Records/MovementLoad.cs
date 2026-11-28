using System;

namespace OpenCommonwealth.Services.Ui.Records;

public static class MovementLoad
{
    public static T WithReadLock<T>(Action enter, Action exit, Func<T> body)
    {
        ArgumentNullException.ThrowIfNull(enter);
        ArgumentNullException.ThrowIfNull(exit);
        ArgumentNullException.ThrowIfNull(body);
        enter();
        try { return body(); }
        finally { exit(); }
    }
}
