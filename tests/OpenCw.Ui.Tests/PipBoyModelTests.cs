using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyModelTests
{
    sealed class FakeSource : IPipBoyTabSource
    {
        readonly List<string> _keys;

        public FakeSource(PipBoyTab tab, params string[] keys)
        {
            Tab = tab;
            _keys = keys.ToList();
            CurrentKey = keys[0];
        }

        public PipBoyTab Tab { get; }

        public void Remove(string key) => _keys.Remove(key);

        public string? CurrentKey { get; set; }

        public bool Select(string key)
        {
            if (!_keys.Contains(key)) return false;
            CurrentKey = key;
            return true;
        }

        public IReadOnlyList<PipBoySearchHit> Search(IReadOnlyList<string> tokens) =>
            _keys.Where(k => ListSearch.Matches(k, tokens)).Select(k => new PipBoySearchHit(Tab, k, k, string.Empty)).ToArray();
    }

    static List<PipBoyEvent> Capture(PipBoyModel model)
    {
        var events = new List<PipBoyEvent>();
        model.Raised += events.Add;
        return events;
    }

    static PipBoyModel Searchable()
    {
        var model = new PipBoyModel();
        model.Register(new FakeSource(PipBoyTab.Inventory, "tonic", "rifle ammo"));
        model.Register(new FakeSource(PipBoyTab.Data, "find the rifle", "radio quest"));
        model.Register(new FakeSource(PipBoyTab.Map, "rifle shop"));
        model.Open();
        return model;
    }

    [Fact]
    public void Instant_transitions_pass_through_opening_then_open_and_closing_then_closed()
    {
        var model = new PipBoyModel();
        var events = Capture(model);

        Assert.True(model.Open());
        Assert.Equal(PipBoyPhase.Open, model.Phase);
        Assert.True(model.Close());
        Assert.Equal(PipBoyPhase.Closed, model.Phase);

        Assert.Equal(new PipBoyEvent[]
        {
            new PipBoyPhaseChanged(PipBoyPhase.Opening),
            new PipBoyPhaseChanged(PipBoyPhase.Open),
            new PipBoyPhaseChanged(PipBoyPhase.Closing),
            new PipBoyPhaseChanged(PipBoyPhase.Closed),
        }, events);
    }

    [Fact]
    public void A_model_with_no_listener_still_opens_and_closes()
    {
        var model = new PipBoyModel();

        Assert.True(model.Open());
        Assert.True(model.CycleTab(1));
        Assert.True(model.Close());
        Assert.Equal(PipBoyPhase.Closed, model.Phase);
    }

    [Fact]
    public void Timed_transitions_block_tab_changes_until_completed_and_can_be_reversed()
    {
        var model = new PipBoyModel { InstantTransitions = false };

        model.Open();
        Assert.Equal(PipBoyPhase.Opening, model.Phase);
        Assert.False(model.SelectTab(PipBoyTab.Map));
        Assert.False(model.CycleTab(1));
        Assert.False(model.Open());

        Assert.True(model.CompleteTransition());
        Assert.Equal(PipBoyPhase.Open, model.Phase);
        Assert.True(model.SelectTab(PipBoyTab.Map));

        model.Close();
        Assert.Equal(PipBoyPhase.Closing, model.Phase);
        Assert.True(model.Open());
        Assert.Equal(PipBoyPhase.Opening, model.Phase);
        model.CompleteTransition();
        Assert.Equal(PipBoyPhase.Open, model.Phase);
        Assert.False(model.CompleteTransition());
    }

    [Fact]
    public void Closing_an_open_menu_while_closed_or_closing_is_a_no_op_and_toggle_flips()
    {
        var model = new PipBoyModel();

        Assert.False(model.Close());
        Assert.True(model.Toggle());
        Assert.Equal(PipBoyPhase.Open, model.Phase);
        Assert.True(model.Toggle());
        Assert.Equal(PipBoyPhase.Closed, model.Phase);
    }

    [Fact]
    public void Cycling_visits_every_tab_in_order_and_wraps_both_ways()
    {
        var model = new PipBoyModel();
        model.Open();
        var seen = new List<PipBoyTab> { model.Tab };

        for (var i = 0; i < 5; i++)
        {
            model.CycleTab(1);
            seen.Add(model.Tab);
        }

        Assert.Equal(new[]
        {
            PipBoyTab.Stats, PipBoyTab.Inventory, PipBoyTab.Data, PipBoyTab.Map, PipBoyTab.Radio, PipBoyTab.Stats,
        }, seen);
        model.CycleTab(-1);
        Assert.Equal(PipBoyTab.Radio, model.Tab);
    }

    [Fact]
    public void Opening_straight_to_a_tab_is_silent_but_later_tab_changes_raise_an_event()
    {
        var model = new PipBoyModel();
        var events = Capture(model);

        model.Open(PipBoyTab.Map);
        Assert.Equal(PipBoyTab.Map, model.Tab);
        Assert.DoesNotContain(events, e => e is PipBoyTabChanged);

        model.SelectTab(PipBoyTab.Radio);
        Assert.Contains(new PipBoyTabChanged(PipBoyTab.Map, PipBoyTab.Radio), events);
        Assert.False(model.SelectTab(PipBoyTab.Radio));
    }

    [Fact]
    public void Cursor_of_each_tab_is_restored_when_the_tab_is_entered_again()
    {
        var model = new PipBoyModel();
        var inventory = new FakeSource(PipBoyTab.Inventory, "a", "b", "c");
        model.Register(inventory);
        model.Open(PipBoyTab.Inventory);
        inventory.Select("c");

        model.SelectTab(PipBoyTab.Map);
        inventory.CurrentKey = "a";
        model.SelectTab(PipBoyTab.Inventory);

        Assert.Equal("c", inventory.CurrentKey);
        Assert.Equal("c", model.RememberedKey(PipBoyTab.Inventory));
    }

    [Fact]
    public void Saved_state_restores_the_tab_and_every_cursor_into_a_fresh_model()
    {
        var first = new PipBoyModel();
        var firstInventory = new FakeSource(PipBoyTab.Inventory, "a", "b", "c");
        var firstData = new FakeSource(PipBoyTab.Data, "x", "y");
        first.Register(firstInventory);
        first.Register(firstData);
        first.Open(PipBoyTab.Data);
        firstInventory.Select("b");
        firstData.Select("y");
        var saved = first.ExportState();

        var second = new PipBoyModel();
        second.ImportState(saved);
        var inventory = new FakeSource(PipBoyTab.Inventory, "a", "b", "c");
        var data = new FakeSource(PipBoyTab.Data, "x", "y");
        second.Register(inventory);
        second.Register(data);

        Assert.Equal(PipBoyTab.Data, second.Tab);
        Assert.Equal("b", inventory.CurrentKey);
        Assert.Equal("y", data.CurrentKey);
    }

    [Fact]
    public void Importing_garbage_state_falls_back_instead_of_corrupting_the_model()
    {
        var model = new PipBoyModel();
        var source = new FakeSource(PipBoyTab.Inventory, "a", "b");
        model.Register(source);

        model.ImportState(new PipBoySavedState((PipBoyTab)99, new Dictionary<PipBoyTab, string>
        {
            [(PipBoyTab)42] = "zzz",
            [PipBoyTab.Inventory] = "missing",
            [PipBoyTab.Map] = string.Empty,
        }));

        Assert.Equal(PipBoyTab.Stats, model.Tab);
        Assert.Equal("a", source.CurrentKey);
        Assert.Null(model.RememberedKey(PipBoyTab.Map));
    }

    [Fact]
    public void Global_search_groups_hits_by_tab_in_tab_order_and_skips_tabs_without_hits()
    {
        var model = Searchable();

        model.Search("RIFLE");

        Assert.Equal(new[] { PipBoyTab.Inventory, PipBoyTab.Data, PipBoyTab.Map }, model.SearchGroups.Select(g => g.Tab));
        Assert.Equal(new[] { "rifle ammo", "find the rifle", "rifle shop" }, model.SearchHits.Select(h => h.Key));
    }

    [Fact]
    public void Jumping_to_a_hit_switches_tab_selects_the_item_and_stepping_wraps_through_hits()
    {
        var model = Searchable();
        model.Search("rifle");

        Assert.True(model.JumpToHit(2));
        Assert.Equal(PipBoyTab.Map, model.Tab);
        Assert.Equal("rifle shop", model.RememberedKey(PipBoyTab.Map));
        Assert.Equal(2, model.HitCursor);

        Assert.True(model.StepHit(1));
        Assert.Equal(PipBoyTab.Inventory, model.Tab);
        Assert.Equal("rifle ammo", model.RememberedKey(PipBoyTab.Inventory));

        Assert.True(model.StepHit(-1));
        Assert.Equal(PipBoyTab.Map, model.Tab);
    }

    [Fact]
    public void Stepping_skips_hits_whose_item_has_since_been_removed_and_fails_only_when_all_are_gone()
    {
        var model = new PipBoyModel();
        var inventory = new FakeSource(PipBoyTab.Inventory, "rifle ammo");
        var data = new FakeSource(PipBoyTab.Data, "find the rifle");
        var map = new FakeSource(PipBoyTab.Map, "rifle shop");
        model.Register(inventory);
        model.Register(data);
        model.Register(map);
        model.Open();
        model.Search("rifle");
        Assert.True(model.JumpToHit(0));

        data.Remove("find the rifle");

        Assert.True(model.StepHit(1));
        Assert.Equal(PipBoyTab.Map, model.Tab);
        Assert.Equal(2, model.HitCursor);
        Assert.True(model.StepHit(-1));
        Assert.Equal(PipBoyTab.Inventory, model.Tab);
        Assert.Equal(0, model.HitCursor);

        inventory.Remove("rifle ammo");
        map.Remove("rifle shop");
        Assert.False(model.StepHit(1));
    }

    [Fact]
    public void Jump_fails_without_changing_tab_when_the_menu_is_closed_or_the_key_is_gone()
    {
        var model = Searchable();
        model.Search("rifle");
        var stale = new PipBoySearchHit(PipBoyTab.Data, "deleted quest", "deleted quest", string.Empty);

        Assert.False(model.JumpTo(stale));
        Assert.Equal(PipBoyTab.Stats, model.Tab);

        model.Close();
        Assert.False(model.JumpToHit(0));
        Assert.Equal(PipBoyTab.Stats, model.Tab);
        Assert.False(model.JumpToHit(99));
    }

    [Fact]
    public void Search_caps_hits_per_tab_and_a_blank_query_clears_everything()
    {
        var model = new PipBoyModel { MaxHitsPerTab = 2 };
        model.Register(new FakeSource(PipBoyTab.Inventory, "gun 1", "gun 2", "gun 3"));
        model.Open();

        model.Search("gun");
        Assert.Equal(2, model.SearchHits.Count);

        model.Search("   ");
        Assert.Empty(model.SearchHits);
        Assert.Empty(model.SearchGroups);
        Assert.Equal(string.Empty, model.SearchQuery);
        Assert.False(model.StepHit(1));
    }
}
