using System;

namespace OpenCommonwealth.Services.Ui.Vats;

// Requires: main thread; finished runs exactly once, whether the model closed first or the menu stack removed the menu first.
public sealed class VatsMenuLifecycle(VatsMenuModel model, Action removeFromStack, Action<VatsOutcome> finished)
{
    private bool _closing;

    public void Attach()
    {
        model.Closed -= OnModelClosed;
        model.Closed += OnModelClosed;
    }

    public void StackRemoved()
    {
        model.Abort();
        model.Closed -= OnModelClosed;
    }

    private void OnModelClosed(VatsOutcome outcome)
    {
        if (_closing)
            return;
        _closing = true;
        removeFromStack();
        finished(outcome);
    }
}
