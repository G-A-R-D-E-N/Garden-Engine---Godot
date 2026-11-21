using System;
using System.Linq;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Lists;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterStatAllocationTests
{
    static readonly GenericCharacterData Data = new();

    static StatAllocationModel Make(bool requireAllSpent = true) =>
        new(Data.Stats, Data.StatPoints, requireAllSpent, Data.StatPresets);

    static StatAllocationModel Custom(StatDef[] stats, int pool, StatPreset[]? presets = null, bool require = true) =>
        new(stats, pool, require, presets);

    [Fact]
    public void Random_edit_sequences_keep_points_conserved_and_values_in_range()
    {
        var model = Make();
        var rng = new Random(7);
        var ids = Data.Stats.Select(s => s.Id).ToArray();

        for (var n = 0; n < 2000; n++)
        {
            var id = ids[rng.Next(ids.Length)];
            switch (rng.Next(8))
            {
                case 0: model.Decrement(id); break;
                case 1: model.SetValue(id, rng.Next(0, 13)); break;
                case 2: model.Undo(); break;
                case 3: model.Redo(); break;
                case 4: if (rng.Next(10) == 0) model.Reset(); break;
                case 5: model.ApplyPreset(rng.Next(2) == 0 ? "Even" : "Focused"); break;
                default: model.Increment(id); break;
            }

            Assert.Equal(model.Pool, model.Spent + model.Remaining);
            Assert.InRange(model.Remaining, 0, model.Pool);
            Assert.Equal(model.Spent, model.Values.Sum(p => p.Value - 1));
            foreach (var stat in Data.Stats) Assert.InRange(model.Value(stat.Id), stat.Min, stat.Max);
        }
    }

    [Fact]
    public void Increment_stops_when_the_pool_is_spent_and_resumes_after_a_decrement()
    {
        var model = Make();
        for (var i = 0; i < 5; i++) Assert.True(model.Increment("s1"));

        Assert.False(model.Increment("s1"));
        Assert.False(model.CanIncrement("s2"));
        Assert.False(model.Increment("s2"));
        Assert.Equal(6, model.Value("s1"));
        Assert.Equal(0, model.Remaining);

        Assert.True(model.Decrement("s1"));
        Assert.True(model.Increment("s2"));
        Assert.Equal(5, model.Value("s1"));
        Assert.Equal(2, model.Value("s2"));
    }

    [Fact]
    public void Values_stop_at_their_own_min_and_max()
    {
        var model = Custom(new[] { new StatDef("a", "A", 1, 2) }, 10);

        Assert.False(model.CanDecrement("a"));
        Assert.False(model.Decrement("a"));
        Assert.True(model.Increment("a"));
        Assert.False(model.CanIncrement("a"));
        Assert.False(model.Increment("a"));
        Assert.False(model.SetValue("a", 3));
        Assert.False(model.SetValue("a", 0));
        Assert.False(model.Increment("missing"));
        Assert.Equal(2, model.Value("a"));
    }

    [Fact]
    public void A_point_cost_above_one_is_charged_per_step_and_blocks_when_unaffordable()
    {
        var model = Custom(new[] { new StatDef("a", "A", 0, 5, 2), new StatDef("b", "B", 0, 5) }, 3);

        Assert.True(model.Increment("a"));
        Assert.Equal(2, model.Spent);
        Assert.Equal(1, model.Remaining);
        Assert.False(model.Increment("a"));
        Assert.True(model.Increment("b"));
        Assert.Equal(0, model.Remaining);

        Assert.True(model.Decrement("a"));
        Assert.Equal(2, model.Remaining);
        Assert.False(model.SetValue("a", 2));
        Assert.True(model.SetValue("a", 1));
    }

    [Fact]
    public void Undo_and_redo_walk_the_edit_history_and_a_new_edit_drops_the_redo_branch()
    {
        var model = Make();
        model.Increment("s1");
        model.Increment("s1");
        model.Increment("s2");

        Assert.True(model.Undo());
        Assert.Equal(1, model.Value("s2"));
        Assert.True(model.Undo());
        Assert.Equal(2, model.Value("s1"));
        Assert.True(model.Undo());
        Assert.Equal(1, model.Value("s1"));
        Assert.False(model.Undo());
        Assert.False(model.CanUndo);

        Assert.True(model.Redo());
        Assert.True(model.Redo());
        Assert.True(model.Redo());
        Assert.False(model.Redo());
        Assert.Equal(3, model.Value("s1"));
        Assert.Equal(2, model.Value("s2"));

        Assert.True(model.Undo());
        Assert.True(model.CanRedo);
        Assert.True(model.Increment("s3"));
        Assert.False(model.CanRedo);
        Assert.Equal(2, model.Value("s3"));
        Assert.Equal(1, model.Value("s2"));
    }

    [Fact]
    public void Rejected_edits_leave_no_undo_entry()
    {
        var model = Make();
        Assert.False(model.Decrement("s1"));
        Assert.False(model.SetValue("s1", 1));
        Assert.False(model.SetValue("s1", 99));
        Assert.False(model.CanUndo);
    }

    [Fact]
    public void Reset_returns_every_stat_to_its_minimum_and_can_be_undone()
    {
        var model = Make();
        Assert.False(model.Reset());
        Assert.False(model.CanUndo);

        model.Increment("s1");
        model.Increment("s2");
        Assert.True(model.Reset());
        Assert.Equal(1, model.Value("s1"));
        Assert.Equal(model.Pool, model.Remaining);

        Assert.True(model.Undo());
        Assert.Equal(2, model.Value("s1"));
        Assert.Equal(2, model.Value("s2"));
        Assert.Equal(3, model.Remaining);
    }

    [Fact]
    public void Presets_apply_as_one_undoable_step_and_default_unlisted_stats_to_min()
    {
        var model = Make();
        model.Increment("s7");

        Assert.True(model.ApplyPreset("Even"));
        Assert.Equal(new[] { 2, 2, 2, 2, 2, 1, 1 }, Data.Stats.Select(s => model.Value(s.Id)).ToArray());
        Assert.Equal(0, model.Remaining);
        Assert.False(model.ApplyPreset("Even"));
        Assert.False(model.ApplyPreset("No such preset"));

        Assert.True(model.Undo());
        Assert.Equal(2, model.Value("s7"));
        Assert.Equal(1, model.Value("s1"));
    }

    [Fact]
    public void Invalid_presets_are_refused_without_touching_the_state()
    {
        var presets = new[]
        {
            new StatPreset("unknown", new System.Collections.Generic.Dictionary<string, int> { ["zz"] = 2 }),
            new StatPreset("range", new System.Collections.Generic.Dictionary<string, int> { ["a"] = 99 }),
            new StatPreset("budget", new System.Collections.Generic.Dictionary<string, int> { ["a"] = 4, ["b"] = 4 }),
        };
        var model = Custom(new[] { new StatDef("a", "A", 0, 5), new StatDef("b", "B", 0, 5) }, 5, presets);
        model.Increment("a");

        Assert.False(model.ApplyPreset("unknown"));
        Assert.False(model.ApplyPreset("range"));
        Assert.False(model.ApplyPreset("budget"));
        Assert.Equal(1, model.Value("a"));
        Assert.Equal(0, model.Value("b"));
        Assert.Equal(4, model.Remaining);
    }

    [Fact]
    public void Confirm_is_refused_while_points_are_unspent_and_locks_the_model_once_accepted()
    {
        var model = Make();
        Assert.Equal(AllocationConfirm.UnspentPoints, model.TryConfirm());
        Assert.False(model.IsConfirmed);
        var validation = model.Validate();
        Assert.False(validation.IsValid);
        Assert.Equal(5, validation.Remaining);
        Assert.Equal(new[] { "5 points unspent." }, validation.Errors);

        model.ApplyPreset("Even");
        Assert.True(model.Validate().IsValid);
        Assert.Equal(AllocationConfirm.Confirmed, model.TryConfirm());
        Assert.True(model.IsConfirmed);

        Assert.False(model.Increment("s1"));
        Assert.False(model.Decrement("s1"));
        Assert.False(model.Undo());
        Assert.False(model.Reset());
        Assert.False(model.Cancel());
        Assert.Equal(AllocationConfirm.AlreadyFinished, model.TryConfirm());
        Assert.Equal(2, model.Value("s1"));
    }

    [Fact]
    public void Confirm_allows_unspent_points_when_the_gate_is_not_required()
    {
        var model = Make(requireAllSpent: false);
        Assert.True(model.Validate().IsValid);
        Assert.Equal(AllocationConfirm.Confirmed, model.TryConfirm());
        Assert.Equal(5, model.Remaining);
    }

    [Fact]
    public void Cancel_locks_the_model_and_cannot_be_followed_by_confirm()
    {
        var model = Make();
        model.Increment("s1");

        Assert.True(model.Cancel());
        Assert.True(model.IsCancelled);
        Assert.False(model.Increment("s1"));
        Assert.False(model.Cancel());
        Assert.Equal(AllocationConfirm.AlreadyFinished, model.TryConfirm());
        Assert.False(model.IsConfirmed);
    }

    [Fact]
    public void Changed_fires_once_per_accepted_edit_and_not_for_rejected_ones()
    {
        var model = Make();
        var count = 0;
        model.Changed += () => count++;

        model.Decrement("s1");
        Assert.Equal(0, count);
        model.Increment("s1");
        model.Undo();
        model.Redo();
        Assert.Equal(3, count);
    }

    [Fact]
    public void Construction_rejects_inconsistent_stat_data()
    {
        Assert.Throws<ArgumentException>(() => Custom(Array.Empty<StatDef>(), 1));
        Assert.Throws<ArgumentException>(() => Custom(new[] { new StatDef("a", "A", 1, 2), new StatDef("a", "B", 1, 2) }, 1));
        Assert.Throws<ArgumentException>(() => Custom(new[] { new StatDef("a", "A", 3, 2) }, 1));
        Assert.Throws<ArgumentException>(() => Custom(new[] { new StatDef("a", "A", 1, 2, 0) }, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Custom(new[] { new StatDef("a", "A", 1, 2) }, -1));
    }

    [Fact]
    public void Every_generic_preset_is_valid_and_confirmable()
    {
        foreach (var preset in Data.StatPresets)
        {
            var model = Make();
            Assert.True(model.ApplyPreset(preset.Name));
            Assert.Equal(AllocationConfirm.Confirmed, model.TryConfirm());
        }
    }

    [Fact]
    public void The_undo_history_is_capped_and_keeps_the_newest_edits()
    {
        var model = Custom(new[] { new StatDef("a", "A", 0, 200) }, 200);
        for (var i = 1; i <= 100; i++) Assert.True(model.SetValue("a", i));

        Assert.True(model.Undo());
        Assert.Equal(99, model.Value("a"));

        var undone = 1;
        while (model.Undo()) undone++;

        Assert.Equal(StatAllocationModel.MaxUndo, undone);
        Assert.Equal(100 - StatAllocationModel.MaxUndo, model.Value("a"));
        Assert.False(model.CanUndo);
    }
}
