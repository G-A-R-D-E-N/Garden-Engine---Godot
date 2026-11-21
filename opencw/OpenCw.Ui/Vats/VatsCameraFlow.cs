using System;

namespace OpenCommonwealth.Services.Ui.Vats;

public static class VatsCameraFlow
{
    // Requires: main thread. The camera is entered only when the launcher opened the menu, and left exactly once when the menu reports its outcome (also when that arrives before launch returns).
    public static bool Open(Func<Action<VatsOutcome>, bool> launch, Action enterCamera, Action exitCamera)
    {
        var finished = false;
        var entered = false;
        var opened = launch(_ =>
        {
            if (finished) return;
            finished = true;
            if (entered) exitCamera();
        });
        if (!opened || finished) return opened;
        entered = true;
        enterCamera();
        return true;
    }
}
