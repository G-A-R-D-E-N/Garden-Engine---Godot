using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Ui;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class UiServicesTests
{
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

    private static ScriptHost Host(EvidenceGate gate, params string[] functions)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("UiScript", functions
            .Select(name => new PapyrusFunction(name, new[] { PapyrusInstruction.Return() }))
            .ToList(), Array.Empty<string>()));
        return new ScriptHost(library, gate);
    }

    private static PapyrusValue Invoke(ScriptHost host, string script, string function, params PapyrusValue[] arguments) =>
        ((INativeTarget)host).Invoke(new NativeId(script, function), NativeTargetKind.Static, PapyrusValue.None, arguments);

    private static ScriptHost CallingHost(EvidenceGate gate, string script, string function)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("UiScript", new[]
        {
            new PapyrusFunction("Test", new[]
            {
                PapyrusInstruction.CallStatic(script, function, PapyrusOperand.Local(0), Array.Empty<PapyrusOperand>()),
                PapyrusInstruction.Return(PapyrusOperand.Local(0)),
            }),
        }, Array.Empty<string>()));
        return new ScriptHost(library, gate);
    }

    [Fact]
    public void MessageShowWithTheRealRegistryStopsWithoutAToken()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var gate = new EvidenceGate(EvidenceRegistry.Load(File.ReadAllText(
            Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json"))));
        var host = CallingHost(gate, "Message", "Show");
        var services = new UiServices(host, new RecordingUiService(), gate);
        services.Attach();
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, "Test");

        Assert.False(result.Completed);
        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Equal(1, services.Refused);
        Assert.Empty(services.Latent);
        Assert.Equal(PapyrusValue.None, result.Value);
    }

    [Fact]
    public void NoUiNativeIsBoundWithoutAService()
    {
        var gate = Gate();
        var host = Host(gate);
        var services = new UiServices(host, null, gate);

        var report = services.Attach();

        Assert.Equal(0, report.Bound);
        Assert.Equal(UiServices.Natives.Count, report.Blocked);
        Assert.All(report.Natives, native => Assert.False(native.Bound));
        Assert.All(report.Natives, native => Assert.Contains("no UI service", native.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public void NotificationAndMessageBoxReachTheService()
    {
        var gate = Gate();
        var host = Host(gate);
        var service = new RecordingUiService();
        var services = new UiServices(host, service, gate);

        Assert.Equal(UiServices.Natives.Count - 1, services.Attach().Bound);

        Invoke(host, "Debug", "Notification", PapyrusValue.FromString("hello"));
        Invoke(host, "Debug", "MessageBox", PapyrusValue.FromString("world"));

        Assert.Equal(new[] { "hello" }, service.Notifications);
        Assert.Equal(new[] { "world" }, service.Messages);
    }

    [Fact]
    public void MessageShowIsLatentAndKeepsItsResult()
    {
        var gate = Gate();
        var host = Host(gate);
        var service = new RecordingUiService();
        var services = new UiServices(host, service, gate);
        services.Attach();

        var token = Invoke(host, "Message", "Show", PapyrusValue.FromString("pick")).Integer;

        Assert.Equal(1, token);
        Assert.Equal(-1, services.ResultOf(token));
        Assert.True(services.Complete(token, 2));
        Assert.Equal(2, services.ResultOf(token));
        Assert.False(services.Complete(999, 1));
    }

    [Fact]
    public void OpenMenuIsBlockedAndABytecodeCallStopsAsUnbound()
    {
        var gate = Gate();
        var host = CallingHost(gate, "UI", "OpenMenu");
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
        Assert.False(result.Completed);
        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains("no native named UI.OpenMenu", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalAndMenuFragmentsRunThroughTheHost()
    {
        var gate = Gate();
        var host = Host(gate, "Fragment_Terminal");
        var services = new UiServices(host, new RecordingUiService(), gate);
        var instance = host.Attach("UiScript");
        host.Initialize(instance);

        Assert.True(services.RunFragment(instance, "Fragment_Terminal"));
        Assert.False(services.RunFragment(instance, "Fragment_Missing"));
        Assert.Equal(1, services.Refused);
    }

    [Fact]
    public void AnUnprovenFragmentKeyRunsNothing()
    {
        var gate = Gate((UiServices.FragmentKey, "UNPROVEN"));
        var host = Host(gate, "Fragment_Terminal");
        var services = new UiServices(host, new RecordingUiService(), gate);
        var instance = host.Attach("UiScript");
        host.Initialize(instance);

        Assert.False(services.RunFragment(instance, "Fragment_Terminal"));
        Assert.Equal(1, services.Refused);
    }

    [Fact]
    public void AnUnprovenBindingKeyBindsNothingEvenWithAService()
    {
        var gate = Gate((UiServices.BindingKey, "UNPROVEN"));
        var host = Host(gate);
        var services = new UiServices(host, new RecordingUiService(), gate);

        var report = services.Attach();

        Assert.Equal(0, report.Bound);
        Assert.Equal(UiServices.Natives.Count, report.Blocked);
        Assert.Contains("not proven", report.Natives[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReportIsDeterministicAndCountsEveryNative()
    {
        var gate = Gate();
        var host = Host(gate);
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
