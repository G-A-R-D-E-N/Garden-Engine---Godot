using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Ui;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class UiServicesTests : IDisposable
{
    private readonly List<IScriptHost> _hosts = new();

    private const string DebugStub =
        "Scriptname Debug\n" +
        "Function Notification(String asNotification) global native\n" +
        "Function MessageBox(String asMessage) global native\n";

    private const string MessageStub =
        "Scriptname Message\n" +
        "Int Function Show(String asText) global native\n";

    private const string UiStub =
        "Scriptname UI\n" +
        "Int Function OpenMenu() global native\n";

    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (EvidenceKeys.NativeTarget, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.ReflectionKey, "ENGINE_POLICY"),
        (UiServices.BindingKey, "PROVEN"),
        (UiServices.LatentKey, "PROVEN"),
        (UiServices.FragmentKey, "PROVEN"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides)
    {
        var rows = Rows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            rows[pair.Key] = pair.Status;
        }
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            TestHosts.Dispose(host);
        }
    }

    private IScriptHost Host(HostKind kind, EvidenceGate gate, string body)
    {
        var host = TestHosts.Create(kind, gate,
            new ScriptSpec("UiScript", "Scriptname UiScript\n" + body),
            new ScriptSpec("Debug", DebugStub),
            new ScriptSpec("Message", MessageStub),
            new ScriptSpec("UI", UiStub));
        _hosts.Add(host);
        return host;
    }

    private static void AssertStopped(VmResult result, HostKind kind)
    {
        Assert.False(result.Completed);
        if (kind == HostKind.CSharp)
        {
            Assert.Equal(VmStopReason.ModelError, result.Reason);
        }
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void MessageShowWithTheRealRegistryStopsWithoutAToken(HostKind kind)
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var gate = new EvidenceGate(EvidenceRegistry.Load(File.ReadAllText(
            Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json"))));
        var host = Host(kind, gate,
            "Int Function Test()\n" +
            "  Int result = Message.Show(\"pick\")\n" +
            "  Return result\n" +
            "EndFunction\n");
        var services = new UiServices(host, new RecordingUiService(), gate);
        services.Attach();
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, "Test");

        Assert.False(result.Completed);
        Assert.NotEqual(VmStopReason.Completed, result.Reason);
        Assert.Contains(result.Reason, new[] { VmStopReason.ModelError, VmStopReason.GateStopped, VmStopReason.NativeFailed });
        Assert.InRange(services.Refused, 0, 1);
        Assert.Empty(services.Latent);
        Assert.Equal(PapyrusValue.None, result.Value);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void NoUiNativeIsBoundWithoutAService(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate, string.Empty);
        var services = new UiServices(host, null, gate);

        var report = services.Attach();

        Assert.Equal(0, report.Bound);
        Assert.Equal(UiServices.Natives.Count, report.Blocked);
        Assert.All(report.Natives, native => Assert.False(native.Bound));
        Assert.All(report.Natives, native => Assert.Contains("no UI service", native.Reason, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void NotificationAndMessageBoxReachTheService(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate,
            "Function Notify()\n" +
            "  Debug.Notification(\"hello\")\n" +
            "  Debug.MessageBox(\"world\")\n" +
            "EndFunction\n");
        var service = new RecordingUiService();
        var services = new UiServices(host, service, gate);

        Assert.Equal(UiServices.Natives.Count - 1, services.Attach().Bound);

        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);
        Assert.True(host.RunEvent(instance, "Notify").Completed);

        Assert.Equal(new[] { "hello" }, service.Notifications);
        Assert.Equal(new[] { "world" }, service.Messages);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void MessageShowIsLatentAndKeepsItsResult(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate,
            "Int Function Ask()\n" +
            "  Int token = Message.Show(\"pick\")\n" +
            "  Return token\n" +
            "EndFunction\n");
        var service = new RecordingUiService();
        var services = new UiServices(host, service, gate);
        services.Attach();
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);

        var asked = host.RunEvent(instance, "Ask");
        Assert.True(asked.Completed);
        var token = asked.Value.Integer;

        Assert.Equal(1, token);
        Assert.Equal(-1, services.ResultOf(token));
        Assert.True(services.Complete(token, 2));
        Assert.Equal(2, services.ResultOf(token));
        Assert.False(services.Complete(999, 1));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void OpenMenuIsBlockedAndABytecodeCallStopsAsUnbound(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate,
            "Int Function Test()\n" +
            "  Int result = UI.OpenMenu()\n" +
            "  Return result\n" +
            "EndFunction\n");
        var service = new RecordingUiService();
        var services = new UiServices(host, service, gate);
        var report = services.Attach();
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, "Test");

        var blocked = Assert.Single(report.Natives.Where(native => !native.Bound));
        Assert.Equal("UI.OpenMenu", blocked.Native);
        Assert.Contains("no Garden implementation", blocked.Reason, StringComparison.Ordinal);
        Assert.Equal(3, report.Bound);
        Assert.Equal(1, report.Blocked);
        AssertStopped(result, kind);
        if (kind == HostKind.CSharp)
        {
            Assert.Contains("no native named UI.OpenMenu", result.Message!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TerminalAndMenuFragmentsRunThroughTheHost(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate, "Function Fragment_Terminal()\nEndFunction\n");
        var services = new UiServices(host, new RecordingUiService(), gate);
        var instance = host.Attach("UiScript");
        host.Initialize(instance);

        Assert.True(services.RunFragment(instance, "Fragment_Terminal"));
        Assert.False(services.RunFragment(instance, "Fragment_Missing"));
        Assert.Equal(1, services.Refused);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenFragmentKeyRunsNothing(HostKind kind)
    {
        var gate = Gate((UiServices.FragmentKey, "UNPROVEN"));
        var host = Host(kind, gate, "Function Fragment_Terminal()\nEndFunction\n");
        var services = new UiServices(host, new RecordingUiService(), gate);
        var instance = host.Attach("UiScript");
        host.Initialize(instance);

        Assert.False(services.RunFragment(instance, "Fragment_Terminal"));
        Assert.Equal(1, services.Refused);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenBindingKeyBindsNothingEvenWithAService(HostKind kind)
    {
        var gate = Gate((UiServices.BindingKey, "UNPROVEN"));
        var host = Host(kind, gate, string.Empty);
        var services = new UiServices(host, new RecordingUiService(), gate);

        var report = services.Attach();

        Assert.Equal(0, report.Bound);
        Assert.Equal(UiServices.Natives.Count, report.Blocked);
        Assert.Contains("not proven", report.Natives[0].Reason, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheReportIsDeterministicAndCountsEveryNative(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate, string.Empty);
        var services = new UiServices(host, new RecordingUiService(), gate);

        var first = services.Attach();
        var second = services.Attach();

        Assert.Equal(first.Natives.Count, first.Bound + first.Blocked);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(64, first.Digest().Length);
        Assert.Equal(UiServices.Natives.Count - 1, first.Bound);
    }
}
