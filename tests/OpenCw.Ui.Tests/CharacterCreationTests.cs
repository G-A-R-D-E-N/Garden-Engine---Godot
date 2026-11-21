using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.MainMenu;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class CharacterCreationTests
{
    static double? Five(string id) => 5;

    static void SpendAll(StatAllocationModel model)
    {
        var left = model.Remaining;
        foreach (var id in PlayerSpecialRows.Ids)
            while (left > 0 && model.Increment(id)) left--;
    }

    [Fact]
    public void TheAllocationStartsAtTheRecordBaseAndOffersTheWholePool()
    {
        var model = CharacterCreation.Build(Five);
        Assert.Equal(CharacterCreation.Pool, model.Pool);
        Assert.All(PlayerSpecialRows.Ids, id => Assert.Equal(5, model.Value(id)));
        Assert.Equal(7, model.Stats.Visible.Count());
        Assert.Equal(AllocationConfirm.UnspentPoints, model.TryConfirm());
    }

    [Fact]
    public void AnUnreadableBaseFallsBackToOneAndIsNeverNegative()
    {
        var model = CharacterCreation.Build(id => id == "Luck" ? double.NaN : id == "Agility" ? 0 : null);
        Assert.All(PlayerSpecialRows.Ids, id => Assert.Equal(CharacterCreation.FallbackBase, model.Value(id)));
    }

    [Fact]
    public void ABaseAtTheCeilingShrinksThePoolSoTheMenuCanStillConfirm()
    {
        var model = CharacterCreation.Build(id => 10);
        Assert.Equal(0, model.Pool);
        Assert.Equal(AllocationConfirm.Confirmed, model.TryConfirm());
    }

    [Fact]
    public void AConfirmedAllocationWritesRaisesOverTheBaseAndTheNameWithoutSpendingPerkPoints()
    {
        var model = CharacterCreation.Build(Five);
        SpendAll(model);
        Assert.Equal(AllocationConfirm.Confirmed, model.TryConfirm());
        var xp = new PlayerExperience();
        var character = new PlayerCharacter();
        CharacterCreation.Pending = true;
        Assert.True(CharacterCreation.Commit(model, "  Nora  ", Five, xp, character));
        Assert.False(CharacterCreation.Pending);
        Assert.Equal(CharacterCreation.Pool, PlayerSpecialRows.Ids.Sum(xp.RaisedBy));
        Assert.All(PlayerSpecialRows.Ids, id => Assert.Equal(model.Value(id) - 5, xp.RaisedBy(id)));
        Assert.Equal(0, xp.PerkPoints);
        Assert.Equal("Nora", character.Name);
    }

    [Fact]
    public void ACancelledAllocationChangesNothingAndStillEndsThePendingCreation()
    {
        var model = CharacterCreation.Build(Five);
        SpendAll(model);
        model.Cancel();
        var xp = new PlayerExperience();
        var character = new PlayerCharacter();
        CharacterCreation.Pending = true;
        Assert.False(CharacterCreation.Commit(model, "Nora", Five, xp, character));
        Assert.False(CharacterCreation.Pending);
        Assert.True(xp.IsEmpty);
        Assert.Null(character.Name);
        Assert.Equal(PlayClock.DefaultCharacterName, character.DisplayName);
    }

    [Fact]
    public void RaisesSurviveASaveAndLoadThroughTheExperienceSection()
    {
        var model = CharacterCreation.Build(Five);
        SpendAll(model);
        model.TryConfirm();
        var xp = new PlayerExperience();
        CharacterCreation.Commit(model, "Nora", Five, xp, new PlayerCharacter());
        var loaded = new PlayerExperience();
        loaded.Apply(xp.Sections());
        Assert.All(PlayerSpecialRows.Ids, id => Assert.Equal(xp.RaisedBy(id), loaded.RaisedBy(id)));
    }

    [Fact]
    public void SetSpecialRaisesRefusesAnUnknownNameOrAnOutOfRangeValueAndKeepsTheOldRaises()
    {
        var xp = new PlayerExperience();
        Assert.True(xp.SetSpecialRaises(new Dictionary<string, int> { ["Luck"] = 3 }));
        Assert.False(xp.SetSpecialRaises(new Dictionary<string, int> { ["Luck"] = 1, ["Health"] = 1 }));
        Assert.False(xp.SetSpecialRaises(new Dictionary<string, int> { ["Luck"] = PlayerExperience.SpecialMax + 1 }));
        Assert.False(xp.SetSpecialRaises(new Dictionary<string, int> { ["Luck"] = -1 }));
        Assert.Equal(3, xp.RaisedBy("Luck"));
    }

    [Fact]
    public void ThePlayerNameRoundTripsAndAnOldSaveLoadsWithTheDefault()
    {
        var character = new PlayerCharacter();
        Assert.Empty(character.Sections());
        Assert.True(character.SetName("Nora"));
        Assert.False(character.SetName("   "));
        Assert.Equal("Nora", character.Name);
        var loaded = new PlayerCharacter();
        loaded.Apply(character.Sections());
        Assert.Equal("Nora", loaded.DisplayName);
        loaded.Apply(new Dictionary<string, byte[]>());
        Assert.Equal((true, PlayClock.DefaultCharacterName), (loaded.IsEmpty, loaded.DisplayName));
    }

    [Fact]
    public void AMalformedNameSectionIsRejectedAndLeavesTheNameUntouched()
    {
        var character = new PlayerCharacter();
        character.SetName("Nora");
        foreach (var bad in new[] { "[]", "{}", "{\"name\":5}", "{\"name\":\"\"}", "not json", "{\"name\":\"a\\u0001b\"}" })
            Assert.Throws<FormatException>(() => character.Apply(new Dictionary<string, byte[]> { [PlayerCharacter.SectionName] = Encoding.UTF8.GetBytes(bad) }));
        Assert.Equal("Nora", character.Name);
    }

    [Fact]
    public void TheLiveStatusHeadingShowsTheChosenName()
    {
        PlayerCharacter.Current.SetName("Nora");
        try { Assert.Equal("Nora", new LivePlayerStatus().PlayerName); }
        finally { PlayerCharacter.Current.Clear(); }
        Assert.Null(new LivePlayerStatus().PlayerName);
    }
}
