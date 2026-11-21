using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Ui;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public partial class UiServicesTests : HostFixture
{
    private const string DebugStub =
        "Scriptname Debug\n" +
        "Function Notification(String asNotification) global native\n" +
        "Function MessageBox(String asMessage) global native\n";

    private const string MessageStub =
        "Scriptname Message\n" +
        "Int Function Show(String asText) global native\n";

    private const string UiStub =
        "Scriptname UI\n" +
        "Function OpenMenu(String asMenu) global native\n" +
        "Bool Function IsMenuOpen(String asMenu) global native\n" +
        "Int Function Get(String asMenu, String asPath) global native\n" +
        "Function Set(String asMenu, String asPath, Int aiValue) global native\n" +
        "Int Function Invoke(String asMenu, String asPath, Int aiValue) global native\n";

    private const string ProbeStub =
        "Scriptname Probe\n" +
        "Function Report(Int aiValue) global native\n";

    private const string AskScript =
        "Function Ask()\n" +
        "  Int choice = Message.Show(\"pick\")\n" +
        "  Probe.Report(choice)\n" +
        "EndFunction\n";

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
        (NativeWait.Key, "APPROXIMATED"),
        (UiServices.BindingKey, "PROVEN"),
        (UiServices.LatentKey, "PROVEN"),
        (UiServices.MenuKey, "PROVEN"),
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

    private static EvidenceGate RealRegistry()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        return new EvidenceGate(EvidenceRegistry.Load(File.ReadAllText(
            Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json"))));
    }

    private IScriptHost Host(HostKind kind, EvidenceGate gate, string body, bool withUi = true)
    {
        var scripts = new List<ScriptSpec>
        {
            new ScriptSpec("UiScript", "Scriptname UiScript\n" + body),
            new ScriptSpec("Debug", DebugStub),
            new ScriptSpec("Message", MessageStub),
            new ScriptSpec("Probe", ProbeStub),
        };
        if (withUi)
        {
            scripts.Add(new ScriptSpec("UI", UiStub));
        }
        return Track(TestHosts.Create(kind, gate, scripts.ToArray()));
    }

    private static List<long> Reports(IScriptHost host)
    {
        var values = new List<long>();
        host.RegisterNative(new NativeId("Probe", "Report"), arguments =>
        {
            values.Add(arguments[0].Integer);
            return PapyrusValue.None;
        });
        return values;
    }

    private static void Run(IScriptHost host)
    {
        for (var step = 0; step < 10; ++step)
        {
            host.RunStep(512);
        }
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void MessageShowWaitsForTheAnswerAndReturnsIt(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate, AskScript);
        var reported = Reports(host);
        var service = new RecordingUiService();
        var services = new UiServices(host, service, gate);
        services.Attach();
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);

        var asked = host.RunEvent(instance, "Ask");

        Assert.Equal(VmStopReason.Waiting, asked.Reason);
        Assert.Equal(new[] { "pick" }, service.Shown);
        Assert.Empty(reported);

        service.Answers[0](2);
        Run(host);

        Assert.Equal(new long[] { 2 }, reported);
        Assert.Equal(0, services.Refused);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnAnswerIsAcceptedOnceAndALaterOneIsCounted(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate, AskScript);
        var reported = Reports(host);
        var service = new RecordingUiService();
        var services = new UiServices(host, service, gate);
        services.Attach();
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Ask").Reason);

        service.Answers[0](2);
        service.Answers[0](3);
        Run(host);

        Assert.Equal(new long[] { 2 }, reported);
        Assert.Equal(1, services.Refused);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnAnswerGivenInsideShowMessageIsCountedAndNeverResumes(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate, AskScript);
        var reported = Reports(host);
        var service = new RecordingUiService { AnswerAtOnce = 1 };
        var services = new UiServices(host, service, gate);
        services.Attach();
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);

        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Ask").Reason);
        Run(host);

        Assert.Equal(1, services.Refused);
        Assert.Equal(1, services.Parked);
        Assert.Empty(reported);
    }






    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void MissingScriptLeavesItsNativesUnbound(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate, string.Empty, withUi: false);
        var services = new UiServices(host, new RecordingUiService(), gate);

        var report = services.Attach();

        Assert.Equal(3, report.Bound);
        Assert.Equal(5, report.Blocked);
        Assert.All(report.Natives.Where(native => native.Native.StartsWith("UI.", StringComparison.Ordinal)),
            native => Assert.Contains("no script named UI", native.Reason, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheUiServiceSupersedesTheBaseDebugSinks(HostKind kind)
    {
        var gate = Gate((NativeSet.TraceKey, "ENGINE_POLICY"), (NativeSet.NotificationKey, "ENGINE_POLICY"),
            (NativeSet.MessageBoxKey, "ENGINE_POLICY"));
        var host = Host(kind, gate,
            "Function Notify()\n" +
            "  Debug.Notification(\"hello\")\n" +
            "EndFunction\n");
        var sinks = new NativeSet(gate);
        sinks.BindBaseSet();
        sinks.Install(host);
        var service = new RecordingUiService();
        var services = new UiServices(host, service, gate);
        services.Attach();
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);

        Assert.True(host.RunEvent(instance, "Notify").Completed);

        Assert.Equal(new[] { "hello" }, service.Notifications);
        Assert.DoesNotContain("notification: hello", sinks.Traces);
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

        Assert.Equal(UiServices.Natives.Count, services.Attach().Bound);

        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);
        Assert.True(host.RunEvent(instance, "Notify").Completed);

        Assert.Equal(new[] { "hello" }, service.Notifications);
        Assert.Equal(new[] { "world" }, service.Messages);
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
        Assert.Equal(UiServices.Natives.Count, first.Bound);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void UiNativesReachTheService(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate,
            "Function Open()\n" +
            "  UI.OpenMenu(\"Inventory\")\n" +
            "EndFunction\n" +
            "Bool Function IsOpen()\n" +
            "  Return UI.IsMenuOpen(\"Inventory\")\n" +
            "EndFunction\n" +
            "Function Write(Int value)\n" +
            "  UI.Set(\"Hud\", \"health\", value)\n" +
            "EndFunction\n" +
            "Int Function Read()\n" +
            "  Return UI.Get(\"Hud\", \"health\")\n" +
            "EndFunction\n" +
            "Int Function Flash()\n" +
            "  Return UI.Invoke(\"Hud\", \"flash\", 7)\n" +
            "EndFunction\n");
        var service = new RecordingUiService();
        var services = new UiServices(host, service, gate);
        Assert.Equal(UiServices.Natives.Count, services.Attach().Bound);
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);

        Assert.True(host.RunEvent(instance, "Open").Completed);
        var open = host.RunEvent(instance, "IsOpen");
        Assert.True(open.Completed);
        Assert.True(open.Value.Flag);
        Assert.True(host.RunEvent(instance, "Write", PapyrusValue.FromInt(40)).Completed);
        var read = host.RunEvent(instance, "Read");
        Assert.True(read.Completed);
        Assert.Equal(40L, read.Value.Integer);
        var flashed = host.RunEvent(instance, "Flash");
        Assert.True(flashed.Completed);
        Assert.Equal(1L, flashed.Value.Integer);

        Assert.Equal(new[] { "Inventory" }, service.Menus);
        var set = Assert.Single(service.Sets);
        Assert.Equal(("Hud", "health"), (set.Menu, set.Path));
        Assert.Equal(40L, set.Value.Integer);
        var call = Assert.Single(service.Invocations);
        Assert.Equal("Hud", call.Item1);
        Assert.Equal("flash", call.Item2, ignoreCase: true);
        Assert.Equal(1, call.Item3);
    }
}
