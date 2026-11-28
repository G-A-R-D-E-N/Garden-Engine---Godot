using System;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class SwfCacheWrite
{
    // Invariant: a cache write must never throw, because the converted scene is already in hand and an unwritable user:// only costs the cache.
    public static bool Try(Func<bool> write, Action<Exception>? failed = null)
    {
        try { return write(); }
        catch (Exception e)
        {
            try { failed?.Invoke(e); } catch (Exception) { }
            return false;
        }
    }
}
