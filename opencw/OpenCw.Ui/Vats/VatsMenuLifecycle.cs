using System;

namespace OpenCommonwealth.Services.Ui.Vats;

public sealed class VatsMenuLifecycle(VatsMenuModel model, Action removeFromStack, Action<VatsOutcome> finished)
{
    private bool _closing;
    private bool _stackRemoving;

    public void Attach()
    {
        model.Closed -= OnModelClosed;
        model.Closed += OnModelClosed;
    }

    public void StackRemoved()
    {
        _stackRemoving = true;
        try { model.Abort(); }
        finally
        {
            model.Closed -= OnModelClosed;
            _stackRemoving = false;
        }
    }

    private void OnModelClosed(VatsOutcome outcome)
    {
        if (_closing)
            return;
        _closing = true;
        try
        {
            if (!_stackRemoving)
                removeFromStack();
        }
        finally { finished(outcome); }
    }
}
