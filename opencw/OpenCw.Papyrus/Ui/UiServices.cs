using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Ui;

/// <summary>
/// The Garden UI service a script reaches through the UI natives.
/// </summary>
public interface IUiService
{
    void Notify(string text);

    void MessageBox(string text);

    // Requires: answer runs after ShowMessage returns; once cancelled fires the box must close and answer must not be called.
    void ShowMessage(string text, Action<int> answer, CancellationToken cancelled);

    void Terminal(string text);

    bool OpenMenu(string menu);

    bool IsMenuOpen(string menu);

    PapyrusValue Get(string menu, string path);

    void Set(string menu, string path, PapyrusValue value);

    PapyrusValue Invoke(string menu, string path, IReadOnlyList<PapyrusValue> arguments);
}

/// <summary>Base for a service with no named menus: every menu native answers a benign default and none throws.</summary>
public abstract class MenulessUiService : IUiService
{
    public abstract void Notify(string text);

    public abstract void MessageBox(string text);

    public abstract void ShowMessage(string text, Action<int> answer, CancellationToken cancelled);

    public abstract void Terminal(string text);

    public bool OpenMenu(string menu) => false;

    public virtual bool IsMenuOpen(string menu) => false;

    public PapyrusValue Get(string menu, string path) => PapyrusValue.None;

    public void Set(string menu, string path, PapyrusValue value)
    {
    }

    public PapyrusValue Invoke(string menu, string path, IReadOnlyList<PapyrusValue> arguments) => PapyrusValue.None;
}

public sealed record UiBinding(string Native, bool Bound, string Reason);

public sealed record UiReport(IReadOnlyList<UiBinding> Natives, int Bound, int Blocked)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"natives={Natives.Count} bound={Bound} blocked={Blocked}\n");
        foreach (var native in Natives)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {native.Native} {(native.Bound ? "bound" : "blocked")} {native.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Sha256Hex.OfText(ToReport());
}

public sealed class UiServices : IDisposable
{
    public const int Dismissed = -1;

    public const string BindingKey = "ui.native.binding";
    public const string LatentKey = "ui.latent.results";
    public const string MenuKey = "ui.menu.natives";

    public static IReadOnlyList<string> Natives { get; } = new[]
    {
        "Debug.Notification",
        "Debug.MessageBox",
        "Message.Show",
        "UI.OpenMenu",
        "UI.IsMenuOpen",
        "UI.Get",
        "UI.Set",
        "UI.Invoke",
    };

    private readonly IScriptHost _host;
    private readonly IUiService? _service;
    private readonly EvidenceGate _gate;
    private readonly TimeSpan? _showTimeout;
    private readonly TimeProvider _clock;
    private readonly Dictionary<long, ParkedShow> _parked = new();
    private readonly object _parkedLock = new();
    private static long s_lastToken;
    private bool _closed;

    // Requires: one instance per host; _parked and _closed are guarded by _parkedLock; Shutdown runs on the host's stepping thread.
    public UiServices(IScriptHost host, IUiService? service, EvidenceGate gate, TimeSpan? showTimeout = null,
        TimeProvider? clock = null)
    {
        if (showTimeout is { } limit && limit <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(showTimeout));
        _host = host;
        _service = service;
        _gate = gate;
        _showTimeout = showTimeout;
        _clock = clock ?? TimeProvider.System;
        _host.LatentAbandoned += OnAbandoned;
    }

    public int Bound { get; private set; }

    public int Blocked { get; private set; }

    public int Refused { get; private set; }

    public int Parked { get { lock (_parkedLock) return _parked.Count; } }

    public int Expired { get; private set; }

    public long LastToken { get; private set; }

    public UiReport Attach()
    {
        var bindings = new List<UiBinding>();
        var bound = 0;
        var blocked = 0;
        var service = _service;
        if (service is null || !_gate.Gate(BindingKey).Allowed)
        {
            foreach (var native in Natives)
            {
                blocked++;
                Blocked++;
                bindings.Add(new UiBinding(native, false, service is null
                    ? "no UI service was supplied, so the native is not bound"
                    : "the UI native binding is not proven, so the native is not bound"));
            }
            return new UiReport(bindings, bound, blocked);
        }

        var menuAllowed = _gate.Gate(MenuKey).Allowed;
        foreach (var native in Natives)
        {
            var reason = Blocker(native, menuAllowed);
            if (reason is not null)
            {
                blocked++;
                Blocked++;
                bindings.Add(new UiBinding(native, false, reason));
                continue;
            }
            Register(native, service);
            bound++;
            Bound++;
            bindings.Add(new UiBinding(native, true, "the native is bound through the Garden UI service"));
        }

        return new UiReport(bindings, bound, blocked);
    }

    /// <summary>Completes a latent Message.Show with the service's answer; an unknown, answered, expired or shut-down token is refused.</summary>
    public bool Complete(long token, int choice)
    {
        bool known;
        lock (_parkedLock) known = _parked.ContainsKey(token);
        if (!known)
        {
            Refused++;
            return false;
        }
        if (!TryComplete(token, choice))
        {
            Refused++;
            Forget(token, cancel: true);
            return false;
        }
        Forget(token);
        return true;
    }

    /// <summary>Resumes every wait older than the timeout with <see cref="Dismissed"/> and cancels its box.</summary>
    public int Expire()
    {
        if (_showTimeout is null) return 0;
        var now = _clock.GetUtcNow();
        long[] overdue;
        lock (_parkedLock)
        {
            if (_parked.Count == 0) return 0;
            overdue = _parked.Where(entry => entry.Value.Deadline <= now).Select(entry => entry.Key).ToArray();
        }
        foreach (var token in overdue)
        {
            Forget(token, cancel: true);
            if (!TryComplete(token, Dismissed)) Refused++;
            Expired++;
        }
        return overdue.Length;
    }

    // Requires: the thread that steps the host; cancels every box, dismisses each wait, refuses later answers and new waits.
    public void Shutdown() => Close(dismiss: true);

    private void Close(bool dismiss)
    {
        KeyValuePair<long, ParkedShow>[] swept;
        lock (_parkedLock)
        {
            _closed = true;
            swept = _parked.ToArray();
            _parked.Clear();
        }
        _host.LatentAbandoned -= OnAbandoned;
        foreach (var (token, entry) in swept)
        {
            Release(entry, cancel: true);
            if (!dismiss) continue;
            try
            {
                _host.CompleteLatent(token, PapyrusValue.FromInt(Dismissed));
            }
            catch (ObjectDisposedException)
            {
                dismiss = false;
            }
        }
    }

    public void Dispose() => Shutdown();

    private string? Blocker(string native, bool menuAllowed)
    {
        if (native.StartsWith("UI.", StringComparison.Ordinal) && !menuAllowed)
        {
            return "the UI menu natives have no allowed evidence key, so the native is not bound";
        }
        var script = native[..native.IndexOf('.')];
        return _host.HasScript(script) ? null : $"the library has no script named {script}, so the native is not bound";
    }

    private void Register(string native, IUiService service)
    {
        switch (native)
        {
            case "Debug.Notification":
                _host.RegisterNative(new NativeId("Debug", "Notification"), arguments =>
                {
                    service.Notify(Text(arguments, 0));
                    return PapyrusValue.None;
                });
                break;
            case "Debug.MessageBox":
                _host.RegisterNative(new NativeId("Debug", "MessageBox"), arguments =>
                {
                    try
                    {
                        service.MessageBox(Text(arguments, 0));
                    }
                    catch (InvalidOperationException)
                    {
                        Refused++;
                        throw;
                    }
                    return PapyrusValue.None;
                });
                break;
            case "Message.Show":
                _host.RegisterNative(new NativeId("Message", "Show"), arguments => Show(service, arguments));
                break;
            case "UI.OpenMenu":
                _host.RegisterNative(new NativeId("UI", "OpenMenu"), arguments =>
                {
                    service.OpenMenu(Text(arguments, 0));
                    return PapyrusValue.None;
                });
                break;
            case "UI.IsMenuOpen":
                _host.RegisterNative(new NativeId("UI", "IsMenuOpen"),
                    arguments => PapyrusValue.FromBool(service.IsMenuOpen(Text(arguments, 0))));
                break;
            case "UI.Get":
                _host.RegisterNative(new NativeId("UI", "Get"),
                    arguments => service.Get(Text(arguments, 0), Text(arguments, 1)));
                break;
            case "UI.Set":
                _host.RegisterNative(new NativeId("UI", "Set"), arguments =>
                {
                    service.Set(Text(arguments, 0), Text(arguments, 1), Arg(arguments, 2));
                    return PapyrusValue.None;
                });
                break;
            case "UI.Invoke":
                _host.RegisterNative(new NativeId("UI", "Invoke"),
                    arguments => service.Invoke(Text(arguments, 0), Text(arguments, 1), arguments.Skip(2).ToArray()));
                break;
            default:
                throw new InvalidOperationException($"the UI native {native} has no registration");
        }
    }

    private PapyrusValue Show(IUiService service, IReadOnlyList<PapyrusValue> arguments)
    {
        if (!_gate.Gate(LatentKey).Allowed)
        {
            Refused++;
            throw new InvalidOperationException("the UI latent result key is not allowed, so Message.Show was refused");
        }
        var wait = _gate.Gate(NativeWait.Key, countApproximation: false);
        if (!wait.Allowed)
        {
            Refused++;
            throw new InvalidOperationException($"{wait.Key}: {wait.Reason}");
        }
        var text = Text(arguments, 0);
        var source = new CancellationTokenSource();
        var cancelled = source.Token;
        long token;
        bool admitted;
        lock (_parkedLock)
        {
            admitted = !_closed;
            token = admitted ? Interlocked.Increment(ref s_lastToken) : 0;
            if (admitted)
                _parked[token] = new ParkedShow(source, _showTimeout is { } limit ? _clock.GetUtcNow() + limit : null);
        }
        if (!admitted)
        {
            source.Dispose();
            Refused++;
            throw new InvalidOperationException("the UI services were shut down, so Message.Show was refused");
        }
        LastToken = token;
        try
        {
            service.ShowMessage(text, choice => Complete(token, choice), cancelled);
        }
        catch
        {
            Refused++;
            Forget(token);
            throw;
        }
        bool closed;
        lock (_parkedLock) closed = _closed;
        if (closed)
        {
            Refused++;
            throw new InvalidOperationException("the UI services were shut down, so Message.Show was refused");
        }
        throw new NativeWait(token);
    }

    private bool TryComplete(long token, int choice)
    {
        try
        {
            return _host.CompleteLatent(token, PapyrusValue.FromInt(choice));
        }
        catch (ObjectDisposedException)
        {
            Close(dismiss: false);
            return false;
        }
    }

    private void OnAbandoned(long token) => Forget(token, cancel: true);

    private void Forget(long token, bool cancel = false)
    {
        ParkedShow? entry;
        lock (_parkedLock)
            if (!_parked.Remove(token, out entry)) return;
        Release(entry, cancel);
    }

    private static void Release(ParkedShow entry, bool cancel)
    {
        try
        {
            if (cancel) entry.Source.Cancel();
        }
        catch (AggregateException)
        {
            // Requires: a throwing cancellation callback must not stop the release of the other waits.
        }
        entry.Source.Dispose();
    }

    private static string Text(IReadOnlyList<PapyrusValue> arguments, int index) =>
        arguments.Count > index && (arguments[index].Kind is PapyrusValueKind.String or PapyrusValueKind.None)
            ? (arguments[index].Text ?? string.Empty)
            : throw new InvalidOperationException($"native argument {index} must be a string");

    private static PapyrusValue Arg(IReadOnlyList<PapyrusValue> arguments, int index) =>
        arguments.Count > index ? arguments[index] : throw new InvalidOperationException($"native argument {index} is missing");

    private sealed record ParkedShow(CancellationTokenSource Source, DateTimeOffset? Deadline);
}
