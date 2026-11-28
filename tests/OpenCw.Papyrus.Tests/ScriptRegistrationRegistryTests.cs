using System;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ScriptRegistrationRegistryTests
{
    private static readonly PapyrusOwnerId OwnerA = PapyrusOwnerId.ForPlacedReference("000800:Quests.esp", "000A00");
    private static readonly PapyrusOwnerId OwnerB = PapyrusOwnerId.ForPlacedReference("000800:Quests.esp", "000A01");

    [Fact]
    public void ARegistrationRetainsItsEventNameAndOwner()
    {
        var registry = new ScriptRegistrationRegistry();
        var custom = registry.Register(OwnerA, ScriptRegistration.ForCustomEvent("OnCustomThing"));

        Assert.Equal("OnCustomThing", custom.Registration.EventName);
        Assert.Equal(OwnerA, custom.Owner);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void MatchingDispatchReturnsOnlyMatchingRegistrations()
    {
        var registry = new ScriptRegistrationRegistry();
        registry.Register(OwnerA, ScriptRegistration.ForCustomEvent("OnCustomThing"));
        registry.Register(OwnerB, ScriptRegistration.ForCustomEvent("OnOtherThing"));

        var matched = registry.MatchCustomEvent("OnCustomThing");

        Assert.Single(matched);
        Assert.Equal(OwnerA, matched[0].Owner);
        Assert.Empty(registry.MatchCustomEvent("NothingRegistersForThis"));
    }

    [Fact]
    public void UnregisterRemovesOnlyTheNamedRegistration()
    {
        var registry = new ScriptRegistrationRegistry();
        var first = registry.Register(OwnerA, ScriptRegistration.ForCustomEvent("One"));
        registry.Register(OwnerA, ScriptRegistration.ForCustomEvent("Two"));
        registry.Register(OwnerB, ScriptRegistration.ForCustomEvent("Three"));

        Assert.True(registry.Unregister(first.Handle));
        Assert.False(registry.Unregister(first.Handle));
        Assert.Equal(2, registry.Count);
        Assert.Empty(registry.MatchCustomEvent("One"));
    }

    [Fact]
    public void APayloadWithoutItsRequiredFieldIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ScriptRegistration.ForCustomEvent("  "));
    }
}
