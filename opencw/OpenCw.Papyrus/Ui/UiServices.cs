using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Ui;

/// <summary>
/// The Garden UI service a script reaches through the UI natives.
/// </summary>
public interface IUiService
{
    void Notify(string text);

    void MessageBox(string text);

    void Terminal(string text);
}

/// <summary>A UI service that records what the scripts asked for.</summary>
public sealed class RecordingUiService : IUiService
{
    private readonly List<string> _notifications = new();
    private readonly List<string> _messages = new();
    private readonly List<string> _terminal = new();

    public IReadOnlyList<string> Notifications => _notifications;

    public IReadOnlyList<string> Messages => _messages;

    public IReadOnlyList<string> TerminalLines => _terminal;

    public void Notify(string text) => _notifications.Add(text);

    public void MessageBox(string text) => _messages.Add(text);

    public void Terminal(string text) => _terminal.Add(text);
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

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

public sealed record UiLatentResult(long Token, int Choice, bool Resolved);

public sealed class UiServices
{
    public const string BindingKey = "ui.native.binding";
    public const string LatentKey = "ui.latent.results";
    public const string FragmentKey = "ui.terminal.fragments";

    public static IReadOnlyList<string> Natives { get; } = new[]
    {
        "Debug.Notification",
        "Debug.MessageBox",
        "Message.Show",
        "UI.OpenMenu",
    };

    private readonly IScriptHost _host;
    private readonly IUiService? _service;
    private readonly EvidenceGate _gate;
    private readonly Dictionary<long, UiLatentResult> _latent = new();
    private long _nextToken = 1;

    public UiServices(IScriptHost host, IUiService? service, EvidenceGate gate)
    {
        _host = host;
        _service = service;
        _gate = gate;
    }

    public int Bound { get; private set; }

    public int Blocked { get; private set; }

    public int Refused { get; private set; }

    public int Pending { get; private set; }

    public string? LastFragmentReason { get; private set; }

    public IReadOnlyDictionary<long, UiLatentResult> Latent => _latent;

    public UiReport Attach()
    {
        var bindings = new List<UiBinding>();
        var bound = 0;
        var blocked = 0;

        foreach (var native in Natives)
        {
            if (_service is null || !_gate.Gate(BindingKey).Allowed)
            {
                blocked++;
                Blocked++;
                bindings.Add(new UiBinding(native, false,
                    _service is null
                        ? "no UI service was supplied, so the native is not bound"
                        : "the UI native binding is not proven, so the native is not bound"));
                continue;
            }

            if (native == "UI.OpenMenu")
            {
                blocked++;
                Blocked++;
                bindings.Add(new UiBinding(native, false, "UI.OpenMenu has no Garden implementation"));
                continue;
            }

            bound++;
            Bound++;
            bindings.Add(new UiBinding(native, true, "the native is bound through the Garden UI service"));
        }

        if (_service is null || !_gate.Gate(BindingKey).Allowed)
        {
            return new UiReport(bindings, bound, blocked);
        }

        _host.RegisterNative(new NativeId("Debug", "Notification"), arguments =>
        {
            _service.Notify(Text(arguments, 0));
            return PapyrusValue.None;
        }, NativeTargetKind.Static, "Debug");

        _host.RegisterNative(new NativeId("Debug", "MessageBox"), arguments =>
        {
            _service.MessageBox(Text(arguments, 0));
            return PapyrusValue.None;
        }, NativeTargetKind.Static, "Debug");

        _host.RegisterNative(new NativeId("Message", "Show"), arguments =>
        {
            if (!_gate.Gate(LatentKey).Allowed)
            {
                Refused++;
                throw new InvalidOperationException("the UI latent result key is not allowed, so Message.Show was refused");
            }
            var token = _nextToken++;
            _latent[token] = new UiLatentResult(token, 0, false);
            return PapyrusValue.FromInt(token);
        }, NativeTargetKind.Static, "Message");

        return new UiReport(bindings, bound, blocked);
    }

    /// <summary>Completes a latent Message.Show with the service's answer.</summary>
    public bool Complete(long token, int choice)
    {
        if (!_latent.ContainsKey(token))
        {
            Refused++;
            return false;
        }
        _latent[token] = new UiLatentResult(token, choice, true);
        return true;
    }

    public int ResultOf(long token) =>
        _latent.TryGetValue(token, out var result) && result.Resolved ? result.Choice : -1;

    /// <summary>Runs a terminal or menu fragment through the shared host.</summary>
    public bool RunFragment(ScriptInstance? instance, string fragment)
    {
        LastFragmentReason = null;
        if (!_gate.Gate(FragmentKey).Allowed || instance is null)
        {
            Refused++;
            return false;
        }
        var result = _host.RunEvent(instance, fragment, Array.Empty<PapyrusValue>());
        if (result.Reason == VmStopReason.Waiting)
        {
            Pending++;
            LastFragmentReason = $"{fragment} latent wait: {result.Message}; later items have no automatic continuation";
            return false;
        }
        if (result.Completed)
        {
            return true;
        }
        Refused++;
        return false;
    }

    private static string Text(IReadOnlyList<PapyrusValue> arguments, int index) =>
        arguments.Count > index ? arguments[index].Text ?? string.Empty : string.Empty;
}
