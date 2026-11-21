using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyMemoryTests
{
    static QuestLogModel Log(bool secondCompleted = false) => new(new[]
    {
        new Quest("q1", "Find the Wanderer", "Main", "Search the old mill", System.Array.Empty<QuestObjective>()),
        new Quest("q2", "Clear the Cellar", "Side", "Rats everywhere", System.Array.Empty<QuestObjective>(), secondCompleted),
    });

    static RadioModel Radio() => new(new[] { new RadioStation("s1", "Alpha FM"), new RadioStation("s2", "Beta Radio") });

    // Detects: a tracked quest being forgotten when the Pip-Boy is rebuilt; the next journal must start with it tracked.
    [Fact]
    public void A_tracked_quest_is_tracked_again_in_the_next_journal()
    {
        var memory = new PipBoyMemory();
        var first = Log();
        first.Select("quest:q2");
        memory.Apply(first.TrackCurrent()!);
        var next = Log();
        memory.Restore(next);
        Assert.Equal("q2", next.TrackedId);
    }

    [Fact]
    public void Untracking_clears_the_memory_and_a_completed_quest_is_not_restored()
    {
        var memory = new PipBoyMemory();
        var log = Log();
        log.Select("quest:q1");
        memory.Apply(log.TrackCurrent()!);
        memory.Apply(log.TrackCurrent()!);
        var next = Log();
        memory.Restore(next);
        Assert.Null(next.TrackedId);
        memory.Apply(new QuestEvent(QuestEventKind.Track, "q2"));
        var done = Log(secondCompleted: true);
        memory.Restore(done);
        Assert.Null(done.TrackedId);
    }

    // Detects: a placed map marker being dropped on reopening the map; the memory must hand back the last waypoint.
    [Fact]
    public void The_last_waypoint_is_kept()
    {
        var memory = new PipBoyMemory();
        memory.SetWaypoint(new MapWaypoint("world", 3, 4));
        Assert.Equal(new MapWaypoint("world", 3, 4), memory.Waypoint);
        memory.SetWaypoint(null);
        Assert.Null(memory.Waypoint);
    }

    // Detects: a chosen station or mute state being forgotten when the radio tab is rebuilt; the next model must start from them.
    [Fact]
    public void The_chosen_station_and_mute_state_return_in_the_next_radio_model()
    {
        var memory = new PipBoyMemory();
        var first = Radio();
        memory.Apply(first.Play("s2")!);
        memory.Apply(first.SetMuted(true)!);
        var next = Radio();
        memory.Restore(next);
        Assert.Equal("s2", next.PlayingId);
        Assert.True(next.Muted);
        memory.Apply(next.Stop()!);
        Assert.Null(memory.RadioStation);
    }

    // Detects: favourite slots living only in one inventory model, so they vanish when the Pip-Boy is reopened; a shared store must carry them to the next model.
    [Fact]
    public void Favourite_slots_survive_a_new_inventory_model()
    {
        var memory = new PipBoyMemory();
        var items = new[] { new InventoryItem("a", "Axe", "Weapons", 1, 1), new InventoryItem("b", "Bat", "Weapons", 1, 1) };
        var first = new InventoryModel(items, store: memory.Favourites);
        first.Select("b");
        first.AssignFavourite(1);
        var next = new InventoryModel(items, store: memory.Favourites);
        Assert.Equal(1, next.SlotOf("b"));
    }

    static PipBoyMemory Filled()
    {
        var memory = new PipBoyMemory();
        var log = Log();
        log.Select("quest:q2");
        memory.Apply(log.TrackCurrent()!);
        memory.SetWaypoint(new MapWaypoint("wrld", 10, -20));
        memory.Apply(new RadioEvent(RadioEventKind.Play, "s2"));
        memory.Apply(new RadioEvent(RadioEventKind.Mute, null));
        memory.Favourites.Save(new[] { "1:a", "2:b" });
        memory.Discover("m1");
        return memory;
    }

    // Detects: Pip-Boy choices lost across a save and load; every field must come back from the section.
    [Fact]
    public void The_memory_round_trips_through_its_save_section()
    {
        var saved = Filled().Sections();
        var loaded = new PipBoyMemory();
        loaded.Load(saved);
        Assert.Equal("q2", loaded.TrackedQuest);
        Assert.Equal(new MapWaypoint("wrld", 10, -20), loaded.Waypoint);
        Assert.Equal("s2", loaded.RadioStation);
        Assert.True(loaded.RadioMuted);
        Assert.Equal(new[] { "1:a", "2:b" }, loaded.Favourites.Load().OrderBy(key => key).ToArray());
        Assert.Equal(new[] { "m1" }, loaded.Discovered);
    }

    // Detects: an old save without the section keeping the previous game's choices; loading it must empty the memory.
    [Fact]
    public void A_save_without_the_section_empties_the_memory()
    {
        var memory = Filled();
        memory.Load(new Dictionary<string, byte[]>());
        Assert.True(memory.IsEmpty);
        Assert.Empty(new PipBoyMemory().Sections());
    }

    // Detects: a malformed section half-applying; it must throw FormatException and leave the memory as it was.
    [Fact]
    public void A_malformed_section_throws_and_changes_nothing()
    {
        var memory = Filled();
        var bad = new Dictionary<string, byte[]> { [PipBoyMemory.SectionName] = System.Text.Encoding.UTF8.GetBytes("{\"favourites\":5}") };
        Assert.Throws<FormatException>(() => memory.Load(bad));
        Assert.Equal("q2", memory.TrackedQuest);
        Assert.Equal(new[] { "m1" }, memory.Discovered);
    }

    // Detects: markers inside the radius staying undiscovered or far ones being revealed; the record flag must stay the base and the ledger only add.
    [Fact]
    public void Markers_within_the_radius_are_discovered_and_overlaid_on_the_record_flags()
    {
        var markers = new[]
        {
            new MapMarker("near", "Near", "location", 100, 100, Discovered: false),
            new MapMarker("far", "Far", "location", 100000, 0, Discovered: false),
            new MapMarker("seen", "Seen", "location", 5000, 5000, Discovered: true),
        };
        Assert.Equal(new[] { "near" }, MapDiscovery.Near(markers, 0, 0));
        var shown = MapDiscovery.Apply(markers, new[] { "near" });
        Assert.Equal(new[] { true, false, true }, shown.Select(m => m.Discovered).ToArray());
    }
}
