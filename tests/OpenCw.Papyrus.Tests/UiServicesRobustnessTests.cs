using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Ui;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public partial class UiServicesTests
{
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class MenulessService : MenulessUiService
    {
        public List<string> Texts { get; } = new();

        public override void Notify(string text) => Texts.Add(text);

        public override void MessageBox(string text) => Texts.Add(text);

        public override void ShowMessage(string text, Action<int> answer, System.Threading.CancellationToken cancelled) =>
            Texts.Add(text);

        public override void Terminal(string text) => Texts.Add(text);
    }

    private (IScriptHost Host, ScriptInstance Instance, List<long> Reported, RecordingUiService Service, UiServices Services)
        Parked(HostKind kind, TimeSpan? timeout = null, TimeProvider? clock = null)
    {
        var gate = Gate();
        var host = Host(kind, gate, AskScript);
        var reported = Reports(host);
        var service = new RecordingUiService();
        var services = new UiServices(host, service, gate, timeout, clock);
        services.Attach();
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Ask").Reason);
        return (host, instance, reported, service, services);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ABoxClosedWithoutAChoiceResumesTheScriptWithTheDismissedValue(HostKind kind)
    {
        var (host, _, reported, service, services) = Parked(kind);
        Assert.Equal(1, services.Parked);

        service.Answers[0](UiServices.Dismissed);
        Run(host);

        Assert.Equal(new long[] { UiServices.Dismissed }, reported);
        Assert.Equal(0, services.Parked);
        Assert.Equal(0, services.Refused);
        Assert.False(service.Cancellations[0].IsCancellationRequested);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AWaitPastItsTimeoutIsDismissedAndItsBoxCancelled(HostKind kind)
    {
        var clock = new ManualClock();
        var (host, _, reported, service, services) = Parked(kind, TimeSpan.FromSeconds(30), clock);

        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, services.Expire());
        Run(host);
        Assert.Empty(reported);
        Assert.Equal(1, services.Parked);
        Assert.False(service.Cancellations[0].IsCancellationRequested);

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, services.Expire());
        Run(host);

        Assert.Equal(new long[] { UiServices.Dismissed }, reported);
        Assert.True(service.Cancellations[0].IsCancellationRequested);
        Assert.Equal(0, services.Parked);
        Assert.Equal(1, services.Expired);
        Assert.Equal(0, services.Refused);
        Assert.Equal(0, services.Expire());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnAnswerAfterTheTimeoutIsStaleAndNeverResumesTwice(HostKind kind)
    {
        var clock = new ManualClock();
        var (host, _, reported, service, services) = Parked(kind, TimeSpan.FromSeconds(5), clock);
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(1, services.Expire());
        Run(host);

        service.Answers[0](2);
        Run(host);

        Assert.Equal(new long[] { UiServices.Dismissed }, reported);
        Assert.Equal(1, services.Refused);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnAnswerThatArrivedTooEarlyIsReleasedByTheTimeout(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate, AskScript);
        var reported = Reports(host);
        var clock = new ManualClock();
        var service = new RecordingUiService { AnswerAtOnce = 1 };
        var services = new UiServices(host, service, gate, TimeSpan.FromSeconds(10), clock);
        services.Attach();
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Ask").Reason);
        Assert.Equal(1, services.Refused);
        Assert.Equal(1, services.Parked);

        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal(1, services.Expire());
        Run(host);

        Assert.Equal(new long[] { UiServices.Dismissed }, reported);
        Assert.Equal(0, services.Parked);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void NoTimeoutMeansAWaitNeverExpires(HostKind kind)
    {
        var clock = new ManualClock();
        var (host, _, reported, _, services) = Parked(kind, null, clock);

        clock.Advance(TimeSpan.FromDays(30));

        Assert.Equal(0, services.Expire());
        Run(host);
        Assert.Empty(reported);
        Assert.Equal(1, services.Parked);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonPositiveTimeoutIsRejected(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate, string.Empty);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new UiServices(host, new RecordingUiService(), gate, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new UiServices(host, new RecordingUiService(), gate, TimeSpan.FromSeconds(-1)));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ShutdownCancelsTheBoxRefusesLaterAnswersAndNewWaits(HostKind kind)
    {
        var (host, _, reported, service, services) = Parked(kind);

        services.Shutdown();

        Assert.True(service.Cancellations[0].IsCancellationRequested);
        Assert.Equal(0, services.Parked);
        Assert.Equal(1, host.WaitingCount);
        service.Answers[0](1);
        Run(host);
        Assert.Empty(reported);
        Assert.Equal(1, services.Refused);

        var later = host.Attach("UiScript");
        Assert.True(host.Initialize(later).Completed);
        Assert.NotEqual(VmStopReason.Waiting, host.RunEvent(later, "Ask").Reason);
        Assert.Equal(new[] { "pick" }, service.Shown);
        Assert.Equal(2, services.Refused);
        services.Shutdown();
        services.Dispose();
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnAnswerAfterTheHostIsDisposedIsRefusedWithoutThrowing(HostKind kind)
    {
        var (host, _, _, service, services) = Parked(kind);
        TestHosts.Dispose(host);

        var failure = Xunit.Record.Exception(() => service.Answers[0](1));

        Assert.Null(failure);
        Assert.Equal(1, services.Refused);
        Assert.Equal(0, services.Parked);
        Assert.True(service.Cancellations[0].IsCancellationRequested);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AServiceThatFailsToShowReleasesTheTokenAndFailsTheCall(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate, AskScript);
        var reported = Reports(host);
        var service = new RecordingUiService { ShowFailure = new InvalidOperationException("no box") };
        var services = new UiServices(host, service, gate);
        services.Attach();
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);

        var asked = host.RunEvent(instance, "Ask");
        Run(host);

        Assert.NotEqual(VmStopReason.Waiting, asked.Reason);
        Assert.Equal(0, services.Parked);
        Assert.Equal(1, services.Refused);
        Assert.Empty(reported);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ATokenFromAnotherUiServicesIsNeverCompleted(HostKind kind)
    {
        var (host, _, reported, service, services) = Parked(kind);
        var other = new UiServices(host, service, Gate());

        Assert.False(other.Complete(services.LastToken, 5));
        Run(host);

        Assert.Equal(1, other.Refused);
        Assert.Empty(reported);
        Assert.Equal(1, services.Parked);
        service.Answers[0](2);
        Run(host);
        Assert.Equal(new long[] { 2 }, reported);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ParkedFollowsTheLifecycleOfTheWait(HostKind kind)
    {
        var (host, _, reported, service, services) = Parked(kind);
        Assert.Equal(1, services.Parked);

        service.Answers[0](0);
        Run(host);

        Assert.Equal(0, services.Parked);
        Assert.Equal(new long[] { 0 }, reported);
        Assert.False(services.Complete(services.LastToken, 0));
        Assert.Equal(1, services.Refused);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TokensNeverRepeatSoASecondServicesOnTheSameHostStillParks(HostKind kind)
    {
        var (host, _, reported, service, first) = Parked(kind);
        first.Shutdown();
        var second = new UiServices(host, service, Gate());
        second.Attach();
        var later = host.Attach("UiScript");
        Assert.True(host.Initialize(later).Completed);

        var asked = host.RunEvent(later, "Ask");

        Assert.Equal(VmStopReason.Waiting, asked.Reason);
        Assert.True(second.LastToken > first.LastToken);
        Assert.Equal(1, second.Parked);
        Assert.Equal(new[] { "pick", "pick" }, service.Shown);
        service.Answers[1](4);
        Run(host);
        Assert.Equal(new long[] { 4 }, reported);
        Assert.Equal(0, second.Refused);
        Assert.Equal(0, second.Parked);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AWaitFromOnInitResumesWhenItsAnswerArrives(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate, "Function OnInit()\n  Int choice = Message.Show(\"boot\")\nEndFunction\n");
        var service = new RecordingUiService();
        var services = new UiServices(host, service, gate);
        services.Attach();
        var instance = host.Attach("UiScript");

        var initialized = host.Initialize(instance);

        Assert.Equal(VmStopReason.Waiting, initialized.Reason);
        Assert.Equal(ScriptLifecycle.Initializing, instance.Lifecycle);
        Assert.Equal(new[] { "boot" }, service.Shown);
        Assert.Equal(1, services.Parked);

        service.Answers[0](1);
        Run(host);

        Assert.Equal(0, services.Refused);
        Assert.Equal(0, services.Parked);
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void MenuNativesOnAServiceWithoutMenusAnswerBenignDefaultsAndNeverFailTheScript(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate,
            "Function Open()\n" +
            "  UI.OpenMenu(\"Inventory\")\n" +
            "EndFunction\n" +
            "Bool Function IsOpen()\n" +
            "  Return UI.IsMenuOpen(\"Inventory\")\n" +
            "EndFunction\n" +
            "Function Write()\n" +
            "  UI.Set(\"Hud\", \"health\", 40)\n" +
            "EndFunction\n" +
            "Int Function Read()\n" +
            "  Return UI.Get(\"Hud\", \"health\")\n" +
            "EndFunction\n" +
            "Int Function Flash()\n" +
            "  Return UI.Invoke(\"Hud\", \"flash\", 7)\n" +
            "EndFunction\n");
        var service = new MenulessService();
        var services = new UiServices(host, service, gate);
        Assert.Equal(UiServices.Natives.Count, services.Attach().Bound);
        var instance = host.Attach("UiScript");
        Assert.True(host.Initialize(instance).Completed);

        Assert.True(host.RunEvent(instance, "Open").Completed);
        var open = host.RunEvent(instance, "IsOpen");
        Assert.True(open.Completed);
        Assert.False(open.Value.Flag);
        Assert.True(host.RunEvent(instance, "Write").Completed);
        Assert.True(host.RunEvent(instance, "Read").Completed);
        Assert.True(host.RunEvent(instance, "Flash").Completed);
        Assert.False(service.OpenMenu("Inventory"));
        Assert.Equal(PapyrusValue.None, service.Get("Hud", "health"));
        Assert.Equal(0, services.Refused);
    }
}
