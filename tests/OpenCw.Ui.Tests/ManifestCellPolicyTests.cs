using OpenCommonwealth.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ManifestCellPolicyTests
{
    private const string Scene = "cells/cell_0_0.tscn";

    [Fact]
    public void Uncompiled_cell_without_a_prior_entry_keeps_its_scene_on_disk()
    {
        var decision = ManifestCellPolicy.Decide(Scene, preservePrevious: true, sourceReadFailed: false, sameCell: false);

        Assert.Equal(Scene, decision.Scene);
        Assert.Equal(ManifestCellPolicy.Metadata.Fresh, decision.Metadata);
    }

    [Fact]
    public void Uncompiled_cell_matching_the_prior_entry_keeps_its_metadata()
    {
        var decision = ManifestCellPolicy.Decide(Scene, preservePrevious: true, sourceReadFailed: false, sameCell: true);

        Assert.Equal(Scene, decision.Scene);
        Assert.Equal(ManifestCellPolicy.Metadata.Previous, decision.Metadata);
    }

    [Fact]
    public void Unreadable_cell_without_a_matching_prior_entry_drops_the_stale_scene()
    {
        var decision = ManifestCellPolicy.Decide(Scene, preservePrevious: true, sourceReadFailed: true, sameCell: false);

        Assert.Null(decision.Scene);
        Assert.Equal(ManifestCellPolicy.Metadata.Cleared, decision.Metadata);
    }

    [Fact]
    public void Unreadable_cell_matching_the_prior_entry_keeps_scene_and_metadata()
    {
        var decision = ManifestCellPolicy.Decide(Scene, preservePrevious: true, sourceReadFailed: true, sameCell: true);

        Assert.Equal(Scene, decision.Scene);
        Assert.Equal(ManifestCellPolicy.Metadata.Previous, decision.Metadata);
    }

    [Fact]
    public void Freshly_compiled_cell_publishes_its_own_scene_and_metadata()
    {
        var decision = ManifestCellPolicy.Decide(Scene, preservePrevious: false, sourceReadFailed: false, sameCell: true);

        Assert.Equal(Scene, decision.Scene);
        Assert.Equal(ManifestCellPolicy.Metadata.Fresh, decision.Metadata);
    }

    [Theory]
    [InlineData("000801:Fallout4.esm", Scene, "000801:FALLOUT4.esm", Scene, true)]
    [InlineData("000801:Fallout4.esm", Scene, "000802:Fallout4.esm", Scene, false)]
    [InlineData("000801:Fallout4.esm", "cells/other.tscn", "000801:Fallout4.esm", Scene, false)]
    [InlineData("000801:Fallout4.esm", null, "000801:Fallout4.esm", Scene, false)]
    [InlineData("000801:Fallout4.esm", Scene, "000801:Fallout4.esm", null, false)]
    public void Same_cell_needs_matching_form_key_and_scene(string? oldKey, string? oldScene, string key, string? scene, bool expected) =>
        Assert.Equal(expected, ManifestCellPolicy.IsSameCell(oldKey, oldScene, key, scene));

    [Fact]
    public void Unreadable_cell_matching_prior_entry_keeps_land_and_ref_counts()
    {
        var decision = ManifestCellPolicy.Decide(Scene, preservePrevious: true, sourceReadFailed: true, sameCell: true);
        var counts = ManifestCellPolicy.PublishCounts(decision.Metadata, cellHasLand: false, cellRefCount: 0, previousHasLand: true, previousRefCount: 42);

        Assert.Equal(Scene, decision.Scene);
        Assert.True(counts.HasLand);
        Assert.Equal(42, counts.RefCount);
    }

    [Fact]
    public void Unreadable_cell_without_a_matching_prior_entry_clears_land_refcount_and_scene()
    {
        var decision = ManifestCellPolicy.Decide(Scene, preservePrevious: true, sourceReadFailed: true, sameCell: false);
        var counts = ManifestCellPolicy.PublishCounts(decision.Metadata, cellHasLand: true, cellRefCount: 19, previousHasLand: true, previousRefCount: 99);

        Assert.Null(decision.Scene);
        Assert.False(counts.HasLand);
        Assert.Equal(0, counts.RefCount);
    }

    [Fact]
    public void Uncompiled_cell_without_a_prior_entry_keeps_its_land_and_ref_counts()
    {
        var decision = ManifestCellPolicy.Decide(Scene, preservePrevious: true, sourceReadFailed: false, sameCell: false);
        var counts = ManifestCellPolicy.PublishCounts(decision.Metadata, cellHasLand: true, cellRefCount: 19, previousHasLand: null, previousRefCount: null);

        Assert.Equal(Scene, decision.Scene);
        Assert.True(counts.HasLand);
        Assert.Equal(19, counts.RefCount);
    }

    [Fact]
    public void Resolve_keeps_previous_scene_and_counts_for_an_unchanged_uncompiled_cell()
    {
        var old = new JObject { ["formKey"] = "A:1", ["scene"] = Scene, ["hasLand"] = true, ["refCount"] = 7 };

        var published = ManifestCellPolicy.Resolve(old, "a:1", Scene, preservePrevious: true, sourceReadFailed: false, hasLand: false, refCount: 0);

        Assert.True(published.SameCell);
        Assert.Equal(Scene, published.Scene);
        Assert.True(published.Counts.HasLand);
        Assert.Equal(7, published.Counts.RefCount);
    }

    [Fact]
    public void Resolve_clears_scene_and_counts_when_the_source_read_failed()
    {
        var published = ManifestCellPolicy.Resolve(null, "A:1", Scene, preservePrevious: false, sourceReadFailed: true, hasLand: true, refCount: 5);

        Assert.False(published.SameCell);
        Assert.Null(published.Scene);
        Assert.False(published.Counts.HasLand);
        Assert.Equal(0, published.Counts.RefCount);
    }
}
