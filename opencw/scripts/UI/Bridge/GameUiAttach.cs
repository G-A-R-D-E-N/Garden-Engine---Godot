using Godot;
using OpenCommonwealth.Services.Papyrus.Ui;
using OpenCommonwealth.Services.Papyrus.Vm;
using System;

namespace OpenCommonwealth.UI.Bridge;

public sealed class GameUiAttachment : IDisposable
{
    private readonly GameUI _ui;
    private readonly SceneTree _tree;
    private readonly Action _frame;
    private readonly Action _exiting;
    private bool _disposed;

    internal GameUiAttachment(GameUI ui, GameUiService service, UiServices seam, UiReport report)
    {
        _ui = ui;
        Service = service;
        Seam = seam;
        Report = report;
        _tree = ui.GetTree();
        _frame = () =>
        {
            Service.Pump();
            Seam.Expire();
        };
        _exiting = Dispose;
        _tree.ProcessFrame += _frame;
        _ui.TreeExiting += _exiting;
    }

    public GameUiService Service { get; }

    public UiServices Seam { get; }

    public UiReport Report { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_tree))
            _tree.ProcessFrame -= _frame;
        if (GodotObject.IsInstanceValid(_ui))
            _ui.TreeExiting -= _exiting;
        Seam.Shutdown();
        Service.Dispose();
    }
}

public static class GameUiAttach
{
    // Requires: not called by default; the caller opens the gate described in the lane report, installs after GameSession.Load (its base Debug sinks must be registered first), and installs once per session.
    public static GameUiAttachment Install(GameUI ui, GameSession session, IToastSink? toasts = null,
        TimeSpan? showTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Install(ui, session.Host, toasts, showTimeout);
    }

    public static GameUiAttachment Install(GameUI ui, IScriptHost host, IToastSink? toasts = null,
        TimeSpan? showTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(host);
        if (!ui.IsInsideTree() || ui.MessageBox is null)
            throw new InvalidOperationException("GameUI must be inside the tree and ready before the script seam is installed");
        var service = new GameUiService(ui, toasts);
        try
        {
            var seam = new UiServices(host, service, host.Gate, showTimeout, messageText: RadicleFormLookup.MessageText);
            var report = seam.Attach();
            return new GameUiAttachment(ui, service, seam, report);
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }
}
