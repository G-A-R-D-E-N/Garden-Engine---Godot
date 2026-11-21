using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class NativePublicationTests : HostFixture
{
    private IScriptHost Host(params ScriptSpec[] scripts) =>
        Track(TestHosts.Create(HostKind.Radicle, TestHosts.Gate((ActivationNativeAdapters.SystemKey, "ENGINE_POLICY")), scripts));

    [Fact]
    public void ANativeForAMissingScriptIsReportedFailedAndStaysFailedOnRetry()
    {
        var host = Host(TestHosts.Stub("Other"));
        var native = new NativeId("Missing", "Fn");
        Assert.True(host.TryRegisterNative(native, _ => PapyrusValue.None));

        var publication = host.PublicationOf(native);
        Assert.Equal(NativePublicationState.Failed, publication.State);
        Assert.Contains("no script named Missing", publication.Failure);
        Assert.False(host.RetryNative(native));
        Assert.Equal(NativePublicationState.Failed, host.PublicationOf(native).State);
        Assert.Equal(NativePublicationState.Unregistered, host.PublicationOf(new NativeId("Missing", "Other")).State);
    }

    [Fact]
    public void AnAdapterIsNotBoundWhenItsNativePublicationFailed()
    {
        var host = Host(TestHosts.Stub("Other"));
        var report = new ActivationNativeAdapters(host, TestHosts.Gate((ActivationNativeAdapters.SystemKey, "ENGINE_POLICY")),
            (_, _, _) => { }, _ => false).Bind();

        Assert.Equal(0, report.Bound);
        Assert.Equal(2, report.Unbound);
        Assert.All(report.Natives, outcome =>
        {
            Assert.Equal(AdapterState.Failed, outcome.State);
            Assert.Contains("no script named ObjectReference", outcome.Reason);
        });
    }

    [Fact]
    public void AnAdapterIsBoundOnceItsNativesArePublished()
    {
        var host = Host(TestHosts.Stub("ObjectReference", null,
            "Function BlockActivation(bool abBlocked = true, bool abHideActivateText = false) native\n" +
            "bool Function IsActivationBlocked() native"));
        var report = new ActivationNativeAdapters(host, TestHosts.Gate((ActivationNativeAdapters.SystemKey, "ENGINE_POLICY")),
            (_, _, _) => { }, _ => false).Bind();

        Assert.Equal(2, report.Bound);
        Assert.All(report.Natives, outcome => Assert.Equal(NativePublicationState.Published, host.PublicationOf(outcome.Family.Native).State));
    }

    [Fact]
    public void ANativeRegisteredInsideACallbackIsPendingUntilTheNextStepPublishesIt()
    {
        var host = Host(TestHosts.Stub("External", null, "Function Answer() global native", "Function Later() global native"),
            new ScriptSpec("Caller", "Scriptname Caller\nFunction Run()\n  External.Answer()\nEndFunction\n"));
        var later = new NativeId("External", "Later");
        NativePublication? during = null;
        host.RegisterNative(new NativeId("External", "Answer"), _ =>
        {
            host.RegisterNative(later, values => PapyrusValue.None);
            during = host.PublicationOf(later);
            return PapyrusValue.None;
        });
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        Assert.True(host.RunEvent(instance, "Run").Completed);

        Assert.Equal(NativePublicationState.Pending, during!.State);
        Assert.Equal(NativePublicationState.Pending, host.PublicationOf(later).State);
        host.RunStep(8);
        Assert.Equal(NativePublicationState.Published, host.PublicationOf(later).State);
        Assert.True(host.RetryNative(later));
    }
}
