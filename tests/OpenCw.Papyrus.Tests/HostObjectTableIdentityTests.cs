using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class HostObjectTableIdentityTests
{
    private const string PlayerRef = "000014:Fallout4.esm";

    private static EngineObjectReference Placed() =>
        new(PapyrusOwnerId.ForPlacedReference("000007:Fallout4.esm", PlayerRef));

    [Fact]
    public void ThePlacedOwnerAndTheBareReferenceKeyShareOneHandle()
    {
        var table = new HostObjectTable();

        var fromEvent = table.Describe(Placed());
        var fromNative = table.Describe(new EngineObjectReference(PlayerRef));

        Assert.Equal(fromEvent.Handle, fromNative.Handle);
        Assert.NotNull(Assert.IsType<EngineObjectReference>(table.Resolve(fromNative)).Owner);
    }

    [Fact]
    public void AnEventArgumentTakesTheTypeAnEarlierNativeResultGaveTheReference()
    {
        var table = new HostObjectTable { Derives = (type, parent) => type == "Actor" && parent == "ObjectReference" };

        var native = table.Describe(new EngineObjectReference(PlayerRef), "Actor");

        Assert.Equal("Actor", table.KnownType(Placed()));
        var argument = table.Describe(Placed(), table.KnownType(Placed()));
        Assert.Equal(native.Handle, argument.Handle);
        Assert.Equal(native.Type, argument.Type);
    }

    [Fact]
    public void ALessDerivedTypeDoesNotReplaceTheMoreDerivedOne()
    {
        var table = new HostObjectTable { Derives = (type, parent) => type == "Actor" && parent == "ObjectReference" };

        table.Describe(new EngineObjectReference(PlayerRef), "Actor");

        Assert.Equal("Actor", table.Describe(Placed(), "ObjectReference").Type);
    }

    [Fact]
    public void ABareReferenceFirstIsUpgradedByTheOwnerCarryingOneAndKeepsItsScriptType()
    {
        var table = new HostObjectTable();

        var bare = table.Describe(new EngineObjectReference(PlayerRef) { ScriptType = "Actor" });
        var owned = table.Describe(Placed());

        var resolved = Assert.IsType<EngineObjectReference>(table.Resolve(owned));
        Assert.Equal(bare.Handle, owned.Handle);
        Assert.NotNull(resolved.Owner);
        Assert.Equal("Actor", resolved.ScriptType);
    }
}
