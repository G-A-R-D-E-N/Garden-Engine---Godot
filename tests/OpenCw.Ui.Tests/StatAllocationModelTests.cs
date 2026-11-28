using OpenCommonwealth.Services.Ui.Character;
using Xunit;

namespace OpenCw.Ui.Tests;

public class StatAllocationModelTests
{
    static StatAllocationModel Make(int pool = 2, bool requireAllSpent = true, StatPreset[]? presets = null) =>
        new(new[] { new StatDef("a", "A", 1, 5), new StatDef("b", "B", 3, 5, 2) }, pool, requireAllSpent, presets);

    [Fact]
    public void Every_stat_starts_at_its_own_minimum_with_the_whole_pool_unspent()
    {
        var model = Make(pool: 4);

        Assert.Equal(new Dictionary<string, int> { ["a"] = 1, ["b"] = 3 }, model.Values);
        Assert.Equal(0, model.Spent);
        Assert.Equal(4, model.Remaining);
        Assert.Equal(4, model.Pool);
        Assert.Equal(2, model.Stats.Visible.Count);
    }

    [Fact]
    public void Spent_counts_steps_above_the_minimum_times_the_point_cost()
    {
        var model = Make(pool: 6);

        model.SetValue("a", 3);
        model.SetValue("b", 4);

        Assert.Equal(new Dictionary<string, int> { ["a"] = 3, ["b"] = 4 }, model.Values);
        Assert.Equal(4, model.Spent);
        Assert.Equal(2, model.Remaining);
    }

    [Fact]
    public void An_unknown_stat_id_is_refused_by_every_edit_and_query_but_value_throws()
    {
        var model = Make();
        var changes = 0;
        model.Changed += () => changes++;

        Assert.False(model.CanIncrement("zz"));
        Assert.False(model.CanDecrement("zz"));
        Assert.False(model.Increment("zz"));
        Assert.False(model.Decrement("zz"));
        Assert.False(model.SetValue("zz", 2));

        Assert.Equal(0, changes);
        Assert.False(model.CanUndo);
        var error = Assert.Throws<ArgumentException>(() => model.Value("zz"));
        Assert.Equal("id", error.ParamName);
    }

    [Fact]
    public void A_stat_whose_point_cost_exceeds_what_is_left_cannot_be_raised()
    {
        var model = Make(pool: 1);

        Assert.True(model.CanIncrement("a"));
        Assert.False(model.CanIncrement("b"));
        Assert.False(model.Increment("b"));
        Assert.False(model.SetValue("b", 4));
        Assert.Equal(3, model.Value("b"));
    }

    [Fact]
    public void Validation_reports_the_remaining_points_as_an_error_only_when_every_point_must_be_spent()
    {
        var strict = Make(pool: 3).Validate();
        var relaxed = Make(pool: 3, requireAllSpent: false).Validate();

        Assert.False(strict.IsValid);
        Assert.Equal(3, strict.Remaining);
        Assert.Equal(new[] { "3 points unspent." }, strict.Errors);

        Assert.True(relaxed.IsValid);
        Assert.Equal(3, relaxed.Remaining);
        Assert.Empty(relaxed.Errors);
    }

    [Fact]
    public void An_empty_pool_is_valid_and_confirmable_straight_away()
    {
        var model = Make(pool: 0);

        var validation = model.Validate();

        Assert.True(validation.IsValid);
        Assert.Equal(0, validation.Remaining);
        Assert.Empty(validation.Errors);
        Assert.Equal(AllocationConfirm.Confirmed, model.TryConfirm());
    }

    [Fact]
    public void Applying_a_preset_that_matches_the_current_values_is_a_no_op_with_no_undo_entry()
    {
        var presets = new[]
        {
            new StatPreset("Start", new Dictionary<string, int> { ["a"] = 1 }),
            new StatPreset("Plus", new Dictionary<string, int> { ["a"] = 2 }),
        };
        var model = Make(presets: presets);
        var changes = 0;
        model.Changed += () => changes++;

        Assert.False(model.ApplyPreset("Start"));
        Assert.False(model.ApplyPreset("Nope"));
        Assert.False(model.CanUndo);
        Assert.Equal(0, changes);

        Assert.True(model.ApplyPreset("Plus"));
        Assert.Equal(2, model.Value("a"));
        Assert.Equal(3, model.Value("b"));
        Assert.Equal(1, changes);
        Assert.False(model.ApplyPreset("Plus"));
        Assert.Equal(new[] { "Start", "Plus" }, model.Presets.Select(p => p.Name));
    }

    [Fact]
    public void Without_presets_the_list_is_empty()
    {
        Assert.Empty(Make().Presets);
    }
}
