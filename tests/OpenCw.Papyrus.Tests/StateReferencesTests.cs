using OpenCommonwealth.Services.Papyrus.Save;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class StateReferencesTests
{
    private const string Quest = "000800:QuestProbe.esp";
    private const string Second = "000802:QuestProbe.esp";

    private static string Instance(string script, string owner, string disc, string variables) =>
        "{\"owner\":{\"kind\":0,\"base\":\"" + owner + "\",\"disc\":\"" + disc + "\"},\"script\":\"" + script + "\",\"state\":\"\",\"variables\":[" + variables + "]}";

    private static string Blob(string instances, string pool = "") =>
        "{\"format\":\"test\",\"version\":1,\"instances\":[" + instances + "],\"pool\":[" + pool + "],\"engine\":[]}";

    [Fact]
    public void ADirectObjectValueNamesTheHolderTheVariableAndTheTarget()
    {
        var blob = Blob(
            Instance("StateScript", Quest, "", "[\"::friend_var\",{\"t\":\"o\",\"i\":1,\"type\":\"StateScript\",\"state\":\"\"}]") + "," +
            Instance("StateScript", Second, "", ""));

        var reference = Assert.Single(StateReferences.Scan(blob));

        Assert.Equal($"StateScript on {Quest}.::friend_var -> StateScript on {Second}", reference.Name);
        Assert.Equal((new SavedInstanceId(0, Quest, "", "StateScript"), new SavedInstanceId(0, Second, "", "StateScript")), (reference.Holder, reference.Target));
        Assert.Equal(("StateScript", ""), (reference.Type, reference.State));
        Assert.False(reference.Nested);
    }

    [Fact]
    public void AnEngineReferenceANoneValueAndAnObjectOutsideTheBlobAreNotListed()
    {
        var blob = Blob(Instance("StateScript", Quest, "", string.Join(",",
            "[\"::a_var\",{\"t\":\"e\",\"h\":\"9223372036854775813\",\"type\":\"\",\"state\":\"\"}]",
            "[\"::b_var\",{\"t\":\"n\"}]",
            "[\"::c_var\",{\"t\":\"o\",\"type\":\"StateScript\",\"state\":\"\"}]",
            "[\"::d_var\",{\"t\":\"o\",\"i\":7,\"type\":\"StateScript\",\"state\":\"\"}]",
            "[\"::e_var\",{\"t\":\"i\",\"v\":3}]")));

        Assert.Empty(StateReferences.Scan(blob));
    }

    [Fact]
    public void AReferenceInsideAnArrayOrStructIsListedAsNested()
    {
        var blob = Blob(
            Instance("StateScript", Quest, "", "[\"::items_var\",{\"t\":\"a\",\"r\":0}],[\"::snap_var\",{\"t\":\"st\",\"r\":1}]") + "," +
            Instance("StateScript", Second, "alias2", ""),
            "{\"k\":\"a\",\"et\":0,\"etn\":\"StateScript\",\"e\":[{\"t\":\"o\",\"i\":1,\"type\":\"StateScript\",\"state\":\"\"}]}," +
            "{\"k\":\"s\",\"t\":\"Pair\",\"m\":[[\"link\",{\"t\":\"o\",\"i\":1,\"type\":\"StateScript\",\"state\":\"\"}]]}");

        var references = StateReferences.Scan(blob);

        Assert.Equal(2, references.Count);
        Assert.All(references, reference => Assert.True(reference.Nested));
        Assert.Equal(new[] { "::items_var", "::snap_var" }, references.Select(reference => reference.Variable));
        Assert.Contains("(inside an array or struct)", references[0].Name);
        Assert.Equal($"StateScript on {Second}:alias2", references[0].Target.Name);
    }

    [Fact]
    public void ASelfReferencingPoolEntryEndsTheScan()
    {
        var blob = Blob(
            Instance("StateScript", Quest, "", "[\"::items_var\",{\"t\":\"a\",\"r\":0}]"),
            "{\"k\":\"a\",\"et\":0,\"etn\":\"\",\"e\":[{\"t\":\"a\",\"r\":0}]}");

        Assert.Empty(StateReferences.Scan(blob));
    }

    [Fact]
    public void ABlobWithNoInstancesHasNoReferences() =>
        Assert.Empty(StateReferences.Scan("{\"format\":\"test\",\"version\":1,\"instances\":[],\"pool\":[],\"engine\":[]}"));
}
