using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ScriptInstanceIdentityTests : HostFixture
{
    private const string IdentitySource =
        "Scriptname IdentityScript\n" +
        "Function OnInit()\n" +
        "EndFunction\n" +
        "Function OnActivate(Int value)\n" +
        "EndFunction\n";

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AttachedInstanceExposesItsScriptNameAndDefinedFunctions(HostKind kind)
    {
        var host = Track(TestHosts.Create(kind, TestHosts.Gate(), new ScriptSpec("IdentityScript", IdentitySource)));
        var instance = host.Attach("IdentityScript");

        Assert.Equal("IdentityScript", instance.ScriptName);
        Assert.True(instance.DefinesFunction("OnActivate"));
        Assert.True(instance.DefinesFunction("onactivate"));
        Assert.False(instance.DefinesFunction("OnMissing"));
    }
}
