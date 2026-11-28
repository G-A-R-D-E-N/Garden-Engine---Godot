using System;

namespace OpenCommonwealth.Services.Ui.Vats;

public static class VatsCameraFlow
{
    public static bool Open<TTarget>(Func<Action<VatsOutcome>, Action<TTarget>, bool> launch, Action<TTarget> enterCamera, Action exitCamera)
        where TTarget : notnull
    {
        var finished = false;
        var entered = false;
        TTarget? target = default;
        var hasTarget = false;
        var opened = launch(_ =>
        {
            if (finished) return;
            finished = true;
            if (entered) exitCamera();
        }, value =>
        {
            target = value;
            hasTarget = true;
            if (entered && !finished) enterCamera(value);
        });
        if (!opened || finished || !hasTarget) return opened;
        entered = true;
        enterCamera(target!);
        return true;
    }
}
