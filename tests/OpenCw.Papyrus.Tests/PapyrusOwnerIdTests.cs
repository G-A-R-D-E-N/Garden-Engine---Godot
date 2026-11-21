using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class PapyrusOwnerIdTests
{
    private const string Base = "000800:Quests.esp";

    [Fact]
    public void TwoPlacedReferencesWithOneBaseStayDistinct()
    {
        var first = PapyrusOwnerId.ForPlacedReference(Base, "000A00");
        var second = PapyrusOwnerId.ForPlacedReference(Base, "000A01");

        Assert.Equal(Base, first.BaseFormKey);
        Assert.Equal(Base, second.BaseFormKey);
        Assert.NotEqual(first, second);
        Assert.NotEqual(first.Key, second.Key);
    }

    [Fact]
    public void KindsWithTheSameBaseStayDistinct()
    {
        var form = PapyrusOwnerId.ForForm(Base);
        var placed = PapyrusOwnerId.ForPlacedReference(Base, "000A00");
        var dynamic = PapyrusOwnerId.ForDynamicReference(Base, "spawn-1");
        var instance = PapyrusOwnerId.ForRuntimeInstance(Base, 1);

        var all = new[] { form, placed, dynamic, instance };
        Assert.Equal(4, all.Distinct().Count());
        Assert.Equal(4, all.Select(owner => owner.Key).Distinct().Count());
    }

    [Fact]
    public void TwoAliasesOfOneQuestStayDistinct()
    {
        var first = PapyrusOwnerId.ForQuestAlias(Base, 3);
        var second = PapyrusOwnerId.ForQuestAlias(Base, 4);

        Assert.NotEqual(first, second);
        Assert.Equal("alias:000800:Quests.esp#3", first.Key);
    }

    [Fact]
    public void IdentityMapsKeepBothOwnersOfOneBaseFormKey()
    {
        var properties = new Dictionary<PapyrusOwnerId, string>();
        var first = PapyrusOwnerId.ForPlacedReference(Base, "000A00");
        var second = PapyrusOwnerId.ForRuntimeInstance(Base, 7);

        properties[first] = "first script";
        properties[second] = "second script";

        Assert.Equal(2, properties.Count);
        Assert.Equal("first script", properties[first]);
        Assert.Equal("second script", properties[second]);
        Assert.Single(properties.Keys.Select(owner => owner.BaseFormKey).Distinct());
    }

    [Fact]
    public void TheSameOwnerHasTheSameKey()
    {
        Assert.Equal(PapyrusOwnerId.ForForm(Base).Key, PapyrusOwnerId.ForForm(Base).Key);
        Assert.Equal(PapyrusOwnerId.ForQuestAlias(Base, 2), PapyrusOwnerId.ForQuestAlias(Base, 2));
        Assert.NotEqual(PapyrusOwnerId.ForQuestAlias(Base, 2), PapyrusOwnerId.ForQuestAlias(Base, 3));
    }

    [Fact]
    public void AnOwnerWithoutABaseFormKeyIsRefused()
    {
        Assert.Throws<ArgumentException>(() => PapyrusOwnerId.ForForm(""));
        Assert.Throws<ArgumentException>(() => PapyrusOwnerId.ForPlacedReference(Base, "  "));
        Assert.Throws<ArgumentException>(() => PapyrusOwnerId.ForDynamicReference("", "token"));
    }
}
