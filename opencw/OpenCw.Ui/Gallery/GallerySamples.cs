using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.MainMenu;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Ui.Gallery;

public sealed record SampleSubtitle(string Speaker, string Text);

public sealed record SampleToast(string Text, ToastKind Kind);

public sealed class SampleSaves : ISaveSource
{
    private readonly List<SaveSlotInfo> _slots = new()
    {
        Slot("save_1", "Save 1", "Character A", 14, 22, "Town B", 20, SaveKind.Manual),
        Slot("save_2", "Quick save", "Character A", 14, 22, "Site C", 21, SaveKind.Quick),
        Slot("save_3", "Autosave", "Character A", 13, 20, "Town B", 19, SaveKind.Auto),
        Slot("save_4", "Save 4", "Character B", 3, 4, "Camp E", 12, SaveKind.Manual),
    };

    public IReadOnlyList<SaveSlotInfo> List() => _slots.ToList();

    public bool Delete(string id) => _slots.RemoveAll(slot => slot.Id == id) > 0;

    private static SaveSlotInfo Slot(string id, string name, string character, int level, int hours, string location,
                                     int day, SaveKind kind) =>
        new(id, name, character, level, TimeSpan.FromHours(hours), location,
            new DateTimeOffset(2026, 1, day, 12, 0, 0, TimeSpan.Zero), "", kind);
}

public static class GallerySamples
{
    public const double SampleWeightCapacity = 100;

    public static MapBounds WorldBounds { get; } = new(0, 0, 1000, 1000);

    public static MapBounds LocalBounds { get; } = new(0, 0, 100, 100);

    public static IReadOnlyList<string> LoadingTips { get; } = new[]
    {
        "Tip 1: sample text shown while loading.",
        "Tip 2: another sample line.",
        "Tip 3: a third sample line.",
    };

    public static IReadOnlyList<SampleSubtitle> Subtitles { get; } = new[]
    {
        new SampleSubtitle("Speaker A", "Sample line one."),
        new SampleSubtitle("Speaker B", "Sample line two, a little longer so the wrapping can be seen."),
        new SampleSubtitle("Speaker A", "Sample line three."),
    };

    public static IReadOnlyList<SampleToast> Toasts { get; } = new[]
    {
        new SampleToast("Sample info toast.", ToastKind.Info),
        new SampleToast("Sample success toast.", ToastKind.Success),
        new SampleToast("Sample discovery toast.", ToastKind.Discovery),
        new SampleToast("Sample quest toast.", ToastKind.Quest),
        new SampleToast("Sample warning toast.", ToastKind.Warning),
        new SampleToast("Sample error toast.", ToastKind.Error),
    };

    public static IReadOnlyList<InventoryItem> Inventory() => new[]
    {
        new InventoryItem("item_a", "Item A", "Category 1", 3.0, 40, 1, Equipped: true, Recent: 6, EquipSlot: "Slot 1",
            Equippable: true, Stats: new[] { new ItemStat("Stat 1", 10), new ItemStat("Stat 2", 4, false) }),
        new InventoryItem("item_b", "Item B", "Category 1", 4.5, 65, 1, Recent: 5, EquipSlot: "Slot 1",
            Equippable: true, Stats: new[] { new ItemStat("Stat 1", 14), new ItemStat("Stat 2", 6, false) }),
        new InventoryItem("item_c", "Item C", "Category 1", 1.0, 25, 1, Recent: 4, EquipSlot: "Slot 2",
            Equippable: true, Stats: new[] { new ItemStat("Stat 1", 3) }),
        new InventoryItem("item_d", "Item D", "Category 2", 0.5, 12, 5, Recent: 3, Usable: true),
        new InventoryItem("item_e", "Item E", "Category 2", 0.2, 8, 3, Recent: 2, Usable: true),
        new InventoryItem("item_f", "Item F", "Category 3", 0.1, 1, 24, Recent: 1),
        new InventoryItem("item_g", "Item G", "Category 3", 6.0, 90, 1),
        new InventoryItem("item_h", "Item H", "Category 2", 0.3, 15, 2, Usable: true),
    };

    public static IReadOnlyList<Quest> Quests() => new[]
    {
        new Quest("quest_a", "Quest A", "Group 1", "Sample quest with a finished step and an optional step.",
            new[]
            {
                new QuestObjective("a1", "Objective 1", true),
                new QuestObjective("a2", "Objective 2"),
                new QuestObjective("a3", "Objective 3", Optional: true),
            }, Recent: 3, MapTarget: "marker_a"),
        new Quest("quest_b", "Quest B", "Group 1", "Sample quest with one open step.",
            new[] { new QuestObjective("b1", "Objective 1") }, Recent: 2, MapTarget: "marker_c"),
        new Quest("quest_c", "Quest C", "Group 2", "Sample quest that is finished.",
            new[] { new QuestObjective("c1", "Objective 1", true) }, Completed: true, Recent: 1),
        new Quest("quest_d", "Quest D", "Group 2", "Sample quest with two steps.",
            new[] { new QuestObjective("d1", "Objective 1"), new QuestObjective("d2", "Objective 2") }, MapTarget: "marker_f"),
    };

    public static IReadOnlyList<Note> Notes() => new[]
    {
        new Note("note_1", "Note 1", "Sample note text.", 2),
        new Note("note_2", "Note 2", "Another sample note.", 1),
    };

    public static IReadOnlyList<MapMarker> Markers() => new[]
    {
        new MapMarker("marker_a", "Town A", "Type 1", 200, 300, true, true),
        new MapMarker("marker_b", "Town B", "Type 1", 700, 250, true, true),
        new MapMarker("marker_c", "Site C", "Type 2", 450, 600),
        new MapMarker("marker_d", "Site D", "Type 2", 800, 800, false),
        new MapMarker("marker_e", "Camp E", "Type 3", 150, 850, true, true),
    };

    public static IReadOnlyList<MapMarker> LocalMarkers() => new[]
    {
        new MapMarker("marker_f", "Cache F", "Type 2", 30, 60),
    };

    public static IReadOnlyList<RadioStation> Stations() => new[]
    {
        new RadioStation("station_1", "Station 1", "Sample station one.", Frequency: 91.5),
        new RadioStation("station_2", "Station 2", "Sample station two.", Frequency: 98.1),
        new RadioStation("station_3", "Station 3", "Sample station three.", Frequency: 104.7),
        new RadioStation("station_4", "Station 4", "Sample station that is not available yet.", Enabled: false),
    };

    public static IReadOnlyList<PipBoy.Workshop> Workshops() => new[]
    {
        new PipBoy.Workshop("workshop_1", "Site F", SampleMetrics(1), Rating: 1, MapTarget: "marker_b"),
        new PipBoy.Workshop("workshop_2", "Site G", SampleMetrics(-1), Rating: -1, MapTarget: "marker_e"),
        new PipBoy.Workshop("workshop_3", "Site H", Raiders: true),
    };

    public static IReadOnlyList<StatEntry> Statistics() => new[]
    {
        new StatEntry("count_1", "Count 1", "12", "Group 1"),
        new StatEntry("count_2", "Count 2", "3.5", "Group 1"),
        new StatEntry("count_3", "Count 3", "208", "Group 2"),
        new StatEntry("count_4", "Count 4", "0", "Group 3"),
    };

    public static InventoryModel NewInventory() =>
        new(Inventory(), 8, null, SampleWeightCapacity);

    public static QuestLogModel NewQuestLog() =>
        new(Quests(), Notes(), new WorkshopLog(Workshops()), new StatisticsLog(Statistics()));

    public static MapModel NewMap() => new(WorldBounds, 800, 500, Markers());

    public static MapModel NewLocalMap() => new(LocalBounds, 800, 500, LocalMarkers(), 0.75, 2.0);

    public static RadioModel NewRadio() => new(Stations());

    public static PlayerStatusModel NewStatus(IPlayerStatusSource source) => new(source);

    private static WorkshopMetric[] SampleMetrics(int rating) =>
    [
        new WorkshopMetric("People", 6, rating),
        new WorkshopMetric("Food", 4.25),
        new WorkshopMetric("Water", null),
        new WorkshopMetric("Power", 12, -rating),
    ];
}

public static class GalleryCharacter
{
    public static StatAllocationModel Allocation(ICharacterDataProvider data) =>
        new(data.Stats, data.StatPoints, data.RequireAllStatPointsSpent, data.StatPresets);

    public static CharacterSheetModel Sheet(ICharacterDataProvider data) =>
        new(data.DerivedStats, data.Stats.ToDictionary(stat => stat.Id, stat => (double)stat.Min));

    public static LevelUpModel LevelUp(ICharacterDataProvider data) =>
        new(data.Perks, new Dictionary<string, int>(), 2, data.PerkRequirements);

    public static IReadOnlyList<FavoriteEntry?> FavouriteSlots() => new FavoriteEntry?[]
    {
        new("Item A", 1, KeyLabel: "1", Equipped: true),
        new("Item B", 3, "Ammo A", 24, "2"),
        null,
        new("Item C", 12, KeyLabel: "4"),
        null,
        new("Item D", 1, KeyLabel: "6", Available: false),
        new("Item E", 2, KeyLabel: "7"),
        null,
        new("Item F", 5, "Ammo B", 8, "9"),
        new("Item G", 1, KeyLabel: "0"),
        null,
        new("Item H", 7, KeyLabel: "="),
    };
}
