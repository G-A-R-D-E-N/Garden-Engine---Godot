using System;
using System.Collections.Generic;
using System.Text;
using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettlerAssignmentsTests
{
    [Fact]
    public void AssigningAWorkObjectGivesTheSettlerExactlyOneAndTheObjectOneOwner()
    {
        var store = new SettlerAssignments();

        Assert.Null(store.Assign("ann", "crop1", false));
        Assert.Null(store.Assign("ann", "post1", false));

        Assert.Equal("post1", store.WorkObjectOf("ann"));
        Assert.Null(store.OwnerOf("crop1"));
        Assert.Equal("ann", store.OwnerOf("post1"));
    }

    [Fact]
    public void AssigningAnOwnedObjectDisplacesThePreviousOwner()
    {
        var store = new SettlerAssignments();
        store.Assign("ann", "crop1", false);

        Assert.Equal("ann", store.Assign("bob", "crop1", false));

        Assert.Null(store.WorkObjectOf("ann"));
        Assert.Equal("bob", store.OwnerOf("crop1"));
    }

    [Fact]
    public void AssigningTheSameObjectAgainDisplacesNobody()
    {
        var store = new SettlerAssignments();
        store.Assign("ann", "crop1", false);

        Assert.Null(store.Assign("ann", "crop1", false));
        Assert.Equal("crop1", store.WorkObjectOf("ann"));
    }

    [Fact]
    public void ABedIsKeptApartFromWorkAndAClaimedBedReleasesTheOldOne()
    {
        var store = new SettlerAssignments();
        store.Assign("ann", "crop1", false);
        store.Assign("ann", "bed1", true);
        store.Assign("ann", "bed2", true);

        Assert.Equal("crop1", store.WorkObjectOf("ann"));
        Assert.Equal("bed2", store.BedOf("ann"));
        Assert.Null(store.OwnerOf("bed1"));
    }

    [Fact]
    public void UnassignClearsWorkButKeepsTheBedAndReportsWhetherAnythingChanged()
    {
        var store = new SettlerAssignments();
        store.Assign("ann", "crop1", false);
        store.Assign("ann", "bed1", true);

        Assert.True(store.Unassign("ann"));
        Assert.False(store.Unassign("ann"));

        Assert.Null(store.WorkObjectOf("ann"));
        Assert.Equal("bed1", store.BedOf("ann"));
    }

    [Fact]
    public void AScrappedObjectOrADeadSettlerLosesTheirAssignments()
    {
        var store = new SettlerAssignments();
        store.Assign("ann", "crop1", false);
        store.Assign("bob", "bed1", true);
        store.Assign("bob", "post1", false);

        Assert.True(store.ObjectGone("crop1"));
        Assert.False(store.ObjectGone("crop1"));
        Assert.True(store.SettlerGone("bob"));

        Assert.True(store.IsEmpty);
    }

    [Fact]
    public void AnEmptyStoreWritesNoSection()
    {
        Assert.Empty(new SettlerAssignments().Sections());
    }

    [Fact]
    public void AssignmentsRoundTripThroughASaveSection()
    {
        var saved = new SettlerAssignments();
        saved.Assign("ann", "crop1", false);
        saved.Assign("ann", "bed1", true);
        saved.Assign("bob", "post1", false);

        var loaded = new SettlerAssignments();
        loaded.Apply(saved.Sections());

        Assert.Equal("crop1", loaded.WorkObjectOf("ann"));
        Assert.Equal("bed1", loaded.BedOf("ann"));
        Assert.Equal("bob", loaded.OwnerOf("post1"));
    }

    [Fact]
    public void LoadingASaveWithoutTheSectionClearsAssignments()
    {
        var store = new SettlerAssignments();
        store.Assign("ann", "crop1", false);

        store.Apply(new Dictionary<string, byte[]>());

        Assert.True(store.IsEmpty);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{\"settler\":\"ann\"}]")]
    [InlineData("[{\"settler\":\"\",\"object\":\"crop1\"}]")]
    [InlineData("[{\"settler\":\"ann\",\"object\":\"a\"},{\"settler\":\"bob\",\"object\":\"a\"}]")]
    [InlineData("[{\"settler\":\"ann\",\"object\":\"a\"},{\"settler\":\"ann\",\"object\":\"b\"}]")]
    [InlineData("[{\"settler\":\"ann\",\"object\":\"a\",\"bed\":\"yes\"}]")]
    [InlineData("[{\"settler\":\"ann\",\"object\":\"a\"},{\"settler\":\"bob\",\"object\":\"a\",\"bed\":true}]")]
    [InlineData("not json")]
    public void AMalformedSectionThrowsAndLeavesTheStoreUntouched(string json)
    {
        var store = new SettlerAssignments();
        store.Assign("ann", "crop1", false);
        var sections = new Dictionary<string, byte[]> { [SettlerAssignments.SectionName] = Encoding.UTF8.GetBytes(json) };

        Assert.Throws<FormatException>(() => store.Apply(sections));

        Assert.Equal("crop1", store.WorkObjectOf("ann"));
    }

    [Fact]
    public void AnObjectHeldAsWorkCannotAlsoBeAssignedAsABedOrTheOtherWayRound()
    {
        var store = new SettlerAssignments();
        store.Assign("ann", "crop1", false);
        store.Assign("bob", "bed1", true);

        Assert.True(store.Conflicts("crop1", true));
        Assert.True(store.Conflicts("bed1", false));
        Assert.False(store.Conflicts("crop1", false));
        Assert.Throws<InvalidOperationException>(() => store.Assign("bob", "crop1", true));
        Assert.Throws<InvalidOperationException>(() => store.Assign("ann", "bed1", false));

        Assert.Equal("crop1", store.WorkObjectOf("ann"));
        Assert.Equal("bed1", store.BedOf("bob"));
        Assert.Null(store.BedOf("ann"));
    }

    [Fact]
    public void SectionRowsAreSortedByObjectIdWhateverTheAssignmentOrder()
    {
        var first = new SettlerAssignments();
        first.Assign("bob", "z-post", false);
        first.Assign("ann", "m-bed", true);
        first.Assign("cy", "a-crop", false);
        var second = new SettlerAssignments();
        second.Assign("cy", "a-crop", false);
        second.Assign("ann", "m-bed", true);
        second.Assign("bob", "z-post", false);

        var text = Encoding.UTF8.GetString(first.Sections()[SettlerAssignments.SectionName]);

        Assert.Equal(text, Encoding.UTF8.GetString(second.Sections()[SettlerAssignments.SectionName]));
        Assert.True(text.IndexOf("a-crop", StringComparison.Ordinal) < text.IndexOf("m-bed", StringComparison.Ordinal));
        Assert.True(text.IndexOf("m-bed", StringComparison.Ordinal) < text.IndexOf("z-post", StringComparison.Ordinal));
    }

    [Fact]
    public void AssignRejectsEmptyIds()
    {
        var store = new SettlerAssignments();

        Assert.Throws<ArgumentException>(() => store.Assign("", "crop1", false));
        Assert.Throws<ArgumentException>(() => store.Assign("ann", "", false));
    }

    [Fact]
    public void ClearEmptiesBothTablesAndTheSavedSection()
    {
        var store = new SettlerAssignments();
        store.Assign("ann", "crop1", false);
        store.Assign("bob", "bed1", true);

        store.Clear();

        Assert.True(store.IsEmpty);
        Assert.Null(store.OwnerOf("crop1"));
        Assert.Null(store.BedOf("bob"));
        Assert.Empty(store.Sections());
    }
}
