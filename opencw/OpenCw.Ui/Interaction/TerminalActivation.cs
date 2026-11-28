using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Terminals;

namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread only. `show` returns false when the menu stack refuses the push.
public sealed class TerminalActivation(ITerminalSource source, Func<TerminalFlowModel, bool> show, Action<string> refuse) : IActivationHandler
{
    public const string InUse = "This terminal is already in use.";

    private readonly HashSet<string> _open = new(StringComparer.Ordinal);

    public bool Handles(ActivationTarget target) => target.Groups.Contains("terminals") && target.RefKey.Length > 0;

    public ActivationResult Activate(ActivationTarget target)
    {
        if (_open.Contains(target.RefKey))
            return Refuse(InUse);
        TerminalFlowModel? flow = TerminalFlowModel.TryOpen(source, target.RefKey, out string reason);
        if (flow is null)
            return Refuse(reason);
        _open.Add(target.RefKey);
        flow.Closed += () => _open.Remove(target.RefKey);
        bool shown;
        try
        {
            shown = show(flow);
        }
        catch
        {
            flow.Close();
            throw;
        }
        if (shown)
            return new ActivationResult(ActivationStatus.Handled);
        flow.Close();
        return new ActivationResult(ActivationStatus.Blocked, "the menu stack refused the terminal");
    }

    private ActivationResult Refuse(string reason)
    {
        refuse(reason);
        return new ActivationResult(ActivationStatus.Blocked, reason, Reported: true);
    }
}
