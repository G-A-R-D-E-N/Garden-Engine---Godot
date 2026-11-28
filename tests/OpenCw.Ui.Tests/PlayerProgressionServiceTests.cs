using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlayerProgressionServiceTests
{
    static readonly string[] Names = { "Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck" };

    static readonly int[] StartingAttributes = { 5, 5, 5, 5, 5, 5, 5 };

    static ProgressionCatalog Catalog(ProgressionXpFormula? formula = null, int? cap = null, params ProgressionPerkDef[] perks) =>
        new(ProgressionCatalog.DefaultSpecialAttributes(), perks, formula, cap);

    static ProgressionCatalog PerkCatalog(int? cap = null) => Catalog(null, cap,
        new ProgressionPerkDef("rifleman", "Rifleman", 1, new[]
        {
            new ProgressionPerkRankDef("Rifleman rank 1."),
            new ProgressionPerkRankDef("Rifleman rank 2.", 5, 4, 2),
        }),
        new ProgressionPerkDef("sniper", "Sniper", 1, new[] { new ProgressionPerkRankDef("Sniper rank 1.", 1, 2) },
            "rifleman", "Rifleman", 2),
        new ProgressionPerkDef("lone", "Lone Wanderer", null, new[] { new ProgressionPerkRankDef("Lone Wanderer rank 1.") }));

    static PlayerProgressionService Service(int level = 1, double xp = 0, int points = 0, int[]? attributes = null,
                                            IReadOnlyDictionary<string, int>? ranks = null, ProgressionCatalog? catalog = null) =>
        new(catalog ?? Catalog(), new ProgressionSaveState(level, xp, points, attributes ?? StartingAttributes, ranks));

    static PerkChartEntry Entry(PlayerProgressionService service, string id) =>
        service.Snapshot.Chart.Entries.First(e => e.Id == id);

    [Fact]
    public void The_default_catalog_is_the_seven_special_attributes_at_rank_ten_for_a_level_one_character()
    {
        var service = new PlayerProgressionService();
        var snapshot = service.Snapshot;

        Assert.Equal(1, snapshot.Level);
        Assert.Equal(0, snapshot.PerkPoints);
        Assert.Equal(0.0, snapshot.XpIntoLevel);
        Assert.Equal(200.0, snapshot.XpForNextLevel);
        Assert.Null(snapshot.LevelCap);
        Assert.True(snapshot.IsValid);
        Assert.Equal(StartingAttributes, snapshot.Chart.Attributes);

        var attributes = snapshot.Chart.Entries.Where(e => e.Kind == PerkChartKind.Attribute).ToArray();
        Assert.Equal(7, attributes.Length);
        Assert.Equal(Enumerable.Range(0, 7).Select(i => $"attr{i}"), attributes.Select(e => e.Id));
        Assert.Equal(Names, attributes.Select(e => e.Name));
        Assert.Equal(Enumerable.Range(0, 7), attributes.Select(e => e.Column!.Value));
        Assert.All(attributes, e => Assert.Equal(10, e.MaxRank));
        Assert.All(attributes, e => Assert.Equal(10, e.Ranks.Count));
        Assert.All(attributes, e => Assert.True(e.Available));
    }

    [Fact]
    public void The_xp_curve_is_two_hundred_plus_seventy_five_per_level_and_guards_bad_formulas()
    {
        var formula = new ProgressionXpFormula();

        Assert.Equal(200.0, formula.XpForNextLevel(1));
        Assert.Equal(275.0, formula.XpForNextLevel(2));
        Assert.Equal(350.0, formula.XpForNextLevel(3));
        Assert.Equal(875.0, formula.XpForNextLevel(10));
        Assert.Equal(200.0, formula.XpForNextLevel(0));
        Assert.Equal(200.0, formula.XpForNextLevel(-4));

        Assert.Equal(250.0, new ProgressionXpFormula(100, 50).XpForNextLevel(4));
        Assert.Equal(350.0, new ProgressionXpFormula(double.NaN, 75).XpForNextLevel(3));
        Assert.Equal(350.0, new ProgressionXpFormula(200, double.PositiveInfinity).XpForNextLevel(3));
        Assert.Equal(350.0, new ProgressionXpFormula(-10, -5).XpForNextLevel(3));
    }

    [Fact]
    public void Awarding_xp_carries_over_several_levels_and_grants_one_point_each()
    {
        var service = new PlayerProgressionService();

        Assert.Equal(2, service.AwardXp(200 + 275 + 10));
        Assert.Equal(3, service.Snapshot.Level);
        Assert.Equal(10.0, service.Snapshot.XpIntoLevel);
        Assert.Equal(350.0, service.Snapshot.XpForNextLevel);
        Assert.Equal(2, service.Snapshot.PerkPoints);

        Assert.Equal(0, service.AwardXp(50));
        Assert.Equal(60.0, service.Snapshot.XpIntoLevel);
        Assert.Equal(3, service.Snapshot.Level);

        Assert.Equal(0, service.AwardXp(289));
        Assert.Equal(349.0, service.Snapshot.XpIntoLevel);
        Assert.Equal(1, service.AwardXp(1));
        Assert.Equal(4, service.Snapshot.Level);
        Assert.Equal(0.0, service.Snapshot.XpIntoLevel);
        Assert.Equal(3, service.Snapshot.PerkPoints);
    }

    [Fact]
    public void The_level_cap_stops_levels_and_points_and_freezes_further_awards()
    {
        var service = Service(catalog: Catalog(null, 3));

        Assert.Equal(2, service.AwardXp(10000));
        Assert.Equal(3, service.Snapshot.Level);
        Assert.Equal(2, service.Snapshot.PerkPoints);
        Assert.Equal(0.0, service.Snapshot.XpIntoLevel);
        Assert.Equal(3, service.Snapshot.LevelCap!.Value);

        var revision = service.Snapshot.Revision;
        var changes = 0;
        service.Changed += () => changes++;
        Assert.Equal(0, service.AwardXp(500));
        Assert.Equal(revision, service.Snapshot.Revision);
        Assert.Equal(0.0, service.Snapshot.XpIntoLevel);
        Assert.Equal(0, changes);

        var capped = Service(catalog: Catalog(null, 1));
        Assert.Equal(0, capped.AwardXp(1000));
        Assert.Equal(1, capped.Snapshot.Level);
        Assert.Equal(0, capped.Snapshot.PerkPoints);
    }

    [Fact]
    public void Perk_points_saturate_at_the_curve_maximum()
    {
        var service = Service(catalog: Catalog(new ProgressionXpFormula(1, 0), 400));

        Assert.Equal(300, service.AwardXp(300));
        Assert.Equal(301, service.Snapshot.Level);
        Assert.Equal(255, ProgressionCurve.MaxPerkPoints);
        Assert.Equal(ProgressionCurve.MaxPerkPoints, service.Snapshot.PerkPoints);

        Assert.Equal(ProgressionCurve.MaxPerkPoints, Service(points: 999).Snapshot.PerkPoints);
    }

    [Fact]
    public void Awarding_a_non_finite_or_non_positive_amount_changes_nothing()
    {
        var service = new PlayerProgressionService();
        var changes = 0;
        service.Changed += () => changes++;
        var revision = service.Snapshot.Revision;

        Assert.Equal(0, service.AwardXp(double.NaN));
        Assert.Equal(0, service.AwardXp(double.PositiveInfinity));
        Assert.Equal(0, service.AwardXp(double.NegativeInfinity));
        Assert.Equal(0, service.AwardXp(0));
        Assert.Equal(0, service.AwardXp(-25));

        Assert.Equal(0, changes);
        Assert.Equal(revision, service.Snapshot.Revision);
        Assert.Equal(0.0, service.Snapshot.XpIntoLevel);
        Assert.Equal(1, service.Snapshot.Level);
        Assert.Equal(0, service.Snapshot.PerkPoints);
    }

    [Fact]
    public void Every_state_change_raises_changed_once_and_moves_the_revision_forward()
    {
        var service = Service(points: 1);
        var revisions = new List<int> { service.Snapshot.Revision };
        service.Changed += () => revisions.Add(service.Snapshot.Revision);

        Assert.Equal(0, service.AwardXp(0));
        Assert.Single(revisions);

        Assert.Equal(0, service.AwardXp(50));
        Assert.Equal(2, revisions.Count);

        Assert.Equal(ChooseResult.Ok, service.TryChoose("attr0", 6));
        Assert.Equal(3, revisions.Count);

        Assert.Equal(ChooseResult.Unknown, service.TryChoose("attr0", 6));
        Assert.Equal(ChooseResult.NoPoints, service.TryChoose("attr1", 6));
        Assert.Equal(ChooseResult.Unknown, service.TryChoose("nope", 1));
        Assert.Equal(3, revisions.Count);

        for (var i = 1; i < revisions.Count; i++) Assert.True(revisions[i] > revisions[i - 1]);
    }

    [Fact]
    public void A_special_attribute_is_spent_one_rank_at_a_time_for_one_point()
    {
        var service = Service(points: 2);
        var changes = 0;
        service.Changed += () => changes++;

        Assert.Equal(ChooseResult.Ok, service.TryChoose("attr0", 6));
        Assert.Equal(6, service.Snapshot.Chart.Attributes[0]);
        Assert.Equal(1, service.Snapshot.PerkPoints);
        Assert.Equal(1, changes);
        Assert.Equal(6, Entry(service, "attr0").CurrentRank);

        Assert.Equal(ChooseResult.Unknown, service.TryChoose("attr0", 8));
        Assert.Equal(ChooseResult.Unknown, service.TryChoose("attr0", 5));
        Assert.Equal(ChooseResult.Unknown, service.TryChoose("attr7", 1));
        Assert.Equal(ChooseResult.Unknown, service.TryChoose("attr-1", 1));
        Assert.Equal(ChooseResult.Unknown, service.TryChoose("attr", 1));
        Assert.Equal(ChooseResult.Unknown, service.TryChoose(string.Empty, 1));
        Assert.Equal(1, changes);
        Assert.Equal(1, service.Snapshot.PerkPoints);

        Assert.Equal(ChooseResult.Ok, service.TryChoose("attr6", 6));
        Assert.Equal(0, service.Snapshot.PerkPoints);
        Assert.Equal(6, service.Snapshot.Chart.Attributes[6]);
    }

    [Fact]
    public void A_maxed_attribute_reports_already_max_before_a_point_shortage()
    {
        var maxed = Service(points: 0, attributes: new[] { 10, 5, 5, 5, 5, 5, 5 });
        Assert.Equal(ChooseResult.AlreadyMax, maxed.TryChoose("attr0", 11));
        Assert.False(Entry(maxed, "attr0").Available);

        var empty = Service(points: 0);
        Assert.Equal(ChooseResult.NoPoints, empty.TryChoose("attr1", 6));
        Assert.Equal(5, empty.Snapshot.Chart.Attributes[1]);
    }

    [Fact]
    public void A_catalog_attribute_with_a_lower_max_rank_caps_spending_and_clamps_restore()
    {
        var attributes = ProgressionCatalog.DefaultSpecialAttributes().Select((def, i) => i == 0 ? def with { MaxRank = 3 } : def).ToArray();
        var catalog = new ProgressionCatalog(attributes, Array.Empty<ProgressionPerkDef>());
        var service = new PlayerProgressionService(catalog, new ProgressionSaveState(1, 0, 5, new[] { 9, 5, 5, 5, 5, 5, 5 }));

        Assert.Equal(3, service.Snapshot.Chart.Attributes[0]);
        Assert.Equal(3, Entry(service, "attr0").MaxRank);
        Assert.Equal(ChooseResult.AlreadyMax, service.TryChoose("attr0", 4));

        var shortCatalog = new ProgressionCatalog(ProgressionCatalog.DefaultSpecialAttributes().Take(3).ToArray(), Array.Empty<ProgressionPerkDef>());
        var shortService = new PlayerProgressionService(shortCatalog);

        Assert.Equal(7, shortService.Snapshot.Chart.Attributes.Count);
        Assert.Equal("Charisma", Entry(shortService, "attr3").Name);
        Assert.Equal(10, Entry(shortService, "attr6").MaxRank);
    }

    [Fact]
    public void Perk_level_attribute_column_and_prerequisite_requirements_gate_the_choice()
    {
        var service = Service(points: 10, attributes: new[] { 5, 3, 5, 5, 5, 5, 5 }, catalog: PerkCatalog());

        Assert.Equal(ChooseResult.Ok, service.TryChoose("rifleman", 1));
        Assert.Equal(1, Entry(service, "rifleman").CurrentRank);
        Assert.Equal(9, service.Snapshot.PerkPoints);

        Assert.Equal(ChooseResult.Locked, service.TryChoose("rifleman", 2));
        Assert.Equal(ChooseResult.Locked, service.TryChoose("sniper", 1));
        Assert.Equal(ChooseResult.Unknown, service.TryChoose("missing", 1));
        Assert.Equal(9, service.Snapshot.PerkPoints);
        Assert.Equal(1, Entry(service, "rifleman").CurrentRank);

        var second = Entry(service, "rifleman").Ranks[1];
        Assert.Contains(second.UnmetOrEmpty, u => u.Kind == UnmetKind.Level && u.Needed == 5 && u.Have == 1);
        Assert.Contains(second.UnmetOrEmpty, u => u.Kind == UnmetKind.Attribute && u.Needed == 4 && u.Have == 3 && u.Name == "Perception");
        Assert.DoesNotContain(second.UnmetOrEmpty, u => u.Kind == UnmetKind.Perk);

        var sniper = Entry(service, "sniper");
        Assert.Contains(sniper.Ranks[0].UnmetOrEmpty, u => u.Kind == UnmetKind.Perk && u.Needed == 2 && u.Have == 1 && u.Name == "Rifleman");
        Assert.DoesNotContain(sniper.Ranks[0].UnmetOrEmpty, u => u.Kind == UnmetKind.Level);
        Assert.False(sniper.Available);
        Assert.False(Entry(service, "rifleman").Available);
    }

    [Fact]
    public void A_multi_point_rank_needs_its_full_cost_and_unlocks_once_every_requirement_is_met()
    {
        var ranks = new Dictionary<string, int> { ["rifleman"] = 1 };
        var rich = Service(level: 20, points: 10, attributes: new[] { 5, 4, 5, 5, 5, 5, 5 }, ranks: ranks, catalog: PerkCatalog());

        Assert.True(Entry(rich, "rifleman").Available);
        Assert.Empty(Entry(rich, "rifleman").Ranks[1].UnmetOrEmpty);

        Assert.Equal(ChooseResult.Ok, rich.TryChoose("rifleman", 2));
        Assert.Equal(8, rich.Snapshot.PerkPoints);
        Assert.Equal(2, Entry(rich, "rifleman").CurrentRank);
        Assert.Equal(ChooseResult.AlreadyMax, rich.TryChoose("rifleman", 3));
        Assert.Equal(8, rich.Snapshot.PerkPoints);

        Assert.True(Entry(rich, "sniper").Available);
        Assert.Equal(ChooseResult.Ok, rich.TryChoose("sniper", 1));
        Assert.Equal(7, rich.Snapshot.PerkPoints);

        var poor = Service(level: 5, points: 1, attributes: new[] { 5, 4, 5, 5, 5, 5, 5 }, ranks: ranks, catalog: PerkCatalog());
        Assert.Equal(ChooseResult.NoPoints, poor.TryChoose("rifleman", 2));
        Assert.Equal(1, poor.Snapshot.PerkPoints);
        Assert.Equal(1, Entry(poor, "rifleman").CurrentRank);
    }

    [Fact]
    public void Perk_entries_carry_their_ranks_requirements_costs_and_column()
    {
        var service = Service(points: 0, catalog: PerkCatalog());

        var rifleman = Entry(service, "rifleman");
        Assert.Equal(PerkChartKind.Perk, rifleman.Kind);
        Assert.Equal(1, rifleman.Column!.Value);
        Assert.Equal(0, rifleman.CurrentRank);
        Assert.Equal(2, rifleman.MaxRank);
        Assert.Equal(2, rifleman.Ranks.Count);
        Assert.Equal("Rifleman rank 1.", rifleman.Ranks[0].Description);
        Assert.Equal(1, rifleman.Ranks[0].Cost);
        Assert.Equal(2, rifleman.Ranks[1].Cost);
        Assert.Equal(5, rifleman.Ranks[1].RequiredLevel);
        Assert.Empty(rifleman.Ranks[0].UnmetOrEmpty);
        Assert.True(rifleman.Available);

        var required = rifleman.Ranks[1].RequiredAttribute;
        Assert.NotNull(required);
        Assert.Equal(1, required!.Value.Column);
        Assert.Equal(4, required.Value.Amount);

        var lone = Entry(service, "lone");
        Assert.Null(lone.Column);
        Assert.Null(lone.Ranks[0].RequiredAttribute);
        Assert.True(lone.Available);
        Assert.DoesNotContain(service.Snapshot.Chart.Entries, e => e.Id == "attr7");
    }

    [Fact]
    public void Capture_and_restore_round_trip_the_whole_state()
    {
        var catalog = PerkCatalog();
        var original = Service(level: 20, xp: 120, points: 3, attributes: new[] { 6, 4, 5, 5, 7, 4, 3 },
                               ranks: new Dictionary<string, int> { ["rifleman"] = 2 }, catalog: catalog);
        var state = original.Capture();

        Assert.Equal(20, state.Level);
        Assert.Equal(120.0, state.XpIntoLevel);
        Assert.Equal(3, state.PerkPoints);
        Assert.Equal(new[] { 6, 4, 5, 5, 7, 4, 3 }, state.Attributes);
        Assert.Equal(2, state.PerkRanks!["rifleman"]);

        var restored = new PlayerProgressionService(catalog, state);

        Assert.Equal(original.Snapshot.Level, restored.Snapshot.Level);
        Assert.Equal(original.Snapshot.XpIntoLevel, restored.Snapshot.XpIntoLevel);
        Assert.Equal(original.Snapshot.PerkPoints, restored.Snapshot.PerkPoints);
        Assert.Equal(original.Snapshot.Chart.Attributes, restored.Snapshot.Chart.Attributes);
        Assert.Equal(original.Snapshot.Chart.Entries.Count, restored.Snapshot.Chart.Entries.Count);
        Assert.Equal(2, restored.Capture().PerkRanks!["rifleman"]);

        var target = new PlayerProgressionService(catalog);
        var changes = 0;
        target.Changed += () => changes++;
        var revision = target.Snapshot.Revision;

        target.Restore(state);

        Assert.Equal(original.Snapshot.Chart.Attributes, target.Snapshot.Chart.Attributes);
        Assert.Equal(original.Snapshot.XpIntoLevel, target.Snapshot.XpIntoLevel);
        Assert.Equal(1, changes);
        Assert.True(target.Snapshot.Revision > revision);
    }

    [Fact]
    public void Restore_clamps_every_out_of_bounds_field_and_drops_unknown_perks()
    {
        var service = new PlayerProgressionService(PerkCatalog(5));
        var changes = 0;
        service.Changed += () => changes++;
        var revision = service.Snapshot.Revision;

        service.Restore(new ProgressionSaveState(0, -25, -4, new[] { 0, 99, 5 }, new Dictionary<string, int>
        {
            ["rifleman"] = 99,
            ["ghost"] = 3,
        }));

        var snapshot = service.Snapshot;
        Assert.Equal(1, snapshot.Level);
        Assert.Equal(0.0, snapshot.XpIntoLevel);
        Assert.Equal(0, snapshot.PerkPoints);
        Assert.Equal(new[] { 1, 10, 5, 1, 1, 1, 1 }, snapshot.Chart.Attributes);
        Assert.Equal(0, Entry(service, "rifleman").CurrentRank);
        Assert.DoesNotContain(snapshot.Chart.Entries, e => e.Id == "ghost");
        Assert.Equal(1, changes);
        Assert.True(snapshot.Revision > revision);

        service.Restore(new ProgressionSaveState(99, 99999, 999, StartingAttributes, new Dictionary<string, int> { ["ghost"] = 1 }));
        Assert.Equal(5, service.Snapshot.Level);
        Assert.Equal(255, service.Snapshot.PerkPoints);
        Assert.Equal(service.Snapshot.XpForNextLevel, service.Snapshot.XpIntoLevel);
        Assert.Equal(0, Entry(service, "rifleman").CurrentRank);

        service.Restore(new ProgressionSaveState(2, double.NaN, 0, StartingAttributes));
        Assert.Equal(0.0, service.Snapshot.XpIntoLevel);
        Assert.Equal(2, service.Snapshot.Level);

        service.Restore(new ProgressionSaveState(2, double.PositiveInfinity, 0, StartingAttributes));
        Assert.Equal(0.0, service.Snapshot.XpIntoLevel);
    }

    [Fact]
    public void Restore_drops_perk_ranks_that_fail_level_special_or_prerequisite_requirements()
    {
        var catalog = PerkCatalog();
        var service = new PlayerProgressionService(catalog);

        service.Restore(new ProgressionSaveState(1, 0, 0, new[] { 5, 3, 5, 5, 5, 5, 5 },
            new Dictionary<string, int> { ["rifleman"] = 2 }));
        Assert.Equal(0, Entry(service, "rifleman").CurrentRank);

        service.Restore(new ProgressionSaveState(5, 0, 0, new[] { 5, 4, 5, 5, 5, 5, 5 },
            new Dictionary<string, int> { ["rifleman"] = 2, ["sniper"] = 1 }));
        Assert.Equal(2, Entry(service, "rifleman").CurrentRank);
        Assert.Equal(1, Entry(service, "sniper").CurrentRank);

        service.Restore(new ProgressionSaveState(5, 0, 0, new[] { 5, 4, 5, 5, 5, 5, 5 },
            new Dictionary<string, int> { ["rifleman"] = 1, ["sniper"] = 1 }));
        Assert.Equal(1, Entry(service, "rifleman").CurrentRank);
        Assert.Equal(0, Entry(service, "sniper").CurrentRank);
    }

    static ProgressionCatalog ChainCatalog() => Catalog(null, null,
        new ProgressionPerkDef("a_child", "Child", null, new[] { new ProgressionPerkRankDef("Child.") }, "z_parent", "Parent", 2),
        new ProgressionPerkDef("m_mid", "Mid", null, new[] { new ProgressionPerkRankDef("Mid.") }, "z_parent", "Parent", 1),
        new ProgressionPerkDef("z_parent", "Parent", null, new[] { new ProgressionPerkRankDef("P1."), new ProgressionPerkRankDef("P2.") }),
        new ProgressionPerkDef("cyc_a", "CycA", null, new[] { new ProgressionPerkRankDef("A.") }, "cyc_b", "CycB", 1),
        new ProgressionPerkDef("cyc_b", "CycB", null, new[] { new ProgressionPerkRankDef("B.") }, "cyc_a", "CycA", 1),
        new ProgressionPerkDef("orphan", "Orphan", null, new[] { new ProgressionPerkRankDef("O.") }, "missing", "Missing", 1));

    [Fact]
    public void Restore_resolves_a_prerequisite_that_sorts_after_its_child()
    {
        var service = new PlayerProgressionService(ChainCatalog());

        service.Restore(new ProgressionSaveState(10, 0, 0, StartingAttributes,
            new Dictionary<string, int> { ["a_child"] = 1, ["m_mid"] = 1, ["z_parent"] = 2 }));

        Assert.Equal(1, Entry(service, "a_child").CurrentRank);
        Assert.Equal(1, Entry(service, "m_mid").CurrentRank);
        Assert.Equal(2, Entry(service, "z_parent").CurrentRank);
    }

    [Fact]
    public void Restore_rejects_cyclic_and_missing_prerequisites()
    {
        var service = new PlayerProgressionService(ChainCatalog());

        service.Restore(new ProgressionSaveState(10, 0, 0, StartingAttributes,
            new Dictionary<string, int> { ["cyc_a"] = 1, ["cyc_b"] = 1, ["orphan"] = 1 }));

        Assert.Equal(0, Entry(service, "cyc_a").CurrentRank);
        Assert.Equal(0, Entry(service, "cyc_b").CurrentRank);
        Assert.Equal(0, Entry(service, "orphan").CurrentRank);
        Assert.True(service.Snapshot.PerkPoints > 0);
    }

    [Fact]
    public void Restore_rejects_a_perk_whose_prerequisite_rank_is_too_low()
    {
        var service = new PlayerProgressionService(ChainCatalog());

        service.Restore(new ProgressionSaveState(10, 0, 0, StartingAttributes,
            new Dictionary<string, int> { ["a_child"] = 1, ["z_parent"] = 1 }));

        Assert.Equal(1, Entry(service, "z_parent").CurrentRank);
        Assert.Equal(0, Entry(service, "a_child").CurrentRank);
    }

    [Fact]
    public void Restore_limits_perk_ranks_to_the_points_the_level_earned()
    {
        var service = new PlayerProgressionService(ChainCatalog());

        service.Restore(new ProgressionSaveState(2, 0, 0, StartingAttributes,
            new Dictionary<string, int> { ["z_parent"] = 2 }));
        Assert.Equal(1, Entry(service, "z_parent").CurrentRank);

        service.Restore(new ProgressionSaveState(2, 0, 1, StartingAttributes,
            new Dictionary<string, int> { ["z_parent"] = 1 }));
        Assert.Equal(0, Entry(service, "z_parent").CurrentRank);

        service.Restore(new ProgressionSaveState(3, 0, 0, new[] { 6, 5, 5, 5, 5, 5, 5 },
            new Dictionary<string, int> { ["z_parent"] = 2 }));
        Assert.Equal(1, Entry(service, "z_parent").CurrentRank);
    }

    [Fact]
    public void Restoring_a_null_state_is_rejected()
    {
        var service = new PlayerProgressionService();

        Assert.Throws<ArgumentNullException>(() => service.Restore(null!));
    }

    [Fact]
    public void Restoring_a_full_xp_bar_below_cap_levels_up_on_next_award()
    {
        var service = new PlayerProgressionService(null, new ProgressionSaveState(2, 275, 0, StartingAttributes));

        var gained = service.AwardXp(10);

        Assert.Equal(1, gained);
        Assert.Equal(3, service.Snapshot.Level);
        Assert.Equal(10.0, service.Snapshot.XpIntoLevel);
        Assert.Equal(1, service.Snapshot.PerkPoints);
    }

    [Fact]
    public void Snapshot_clamps_non_positive_perk_rank_costs_to_match_spending()
    {
        var catalog = Catalog(null, null,
            new ProgressionPerkDef("cheap", "Cheap", null, new[] { new ProgressionPerkRankDef("Free rank", Cost: 0) }));
        var service = new PlayerProgressionService(catalog);

        Assert.Equal(1, Entry(service, "cheap").Ranks[0].Cost);
    }

    [Fact]
    public void Huge_xp_and_max_level_terminate_without_overflow()
    {
        var huge = new PlayerProgressionService();
        var gainedHuge = huge.AwardXp(1e20);
        Assert.True(gainedHuge > 1);
        Assert.True(huge.Snapshot.Level > 1);
        Assert.True(huge.Snapshot.IsValid);

        var maxLevel = new PlayerProgressionService(null, new ProgressionSaveState(int.MaxValue, 0, 0, StartingAttributes));
        var gained = maxLevel.AwardXp(10000);
        Assert.Equal(0, gained);
        Assert.Equal(int.MaxValue, maxLevel.Snapshot.Level);
        Assert.True(maxLevel.Snapshot.IsValid);
    }
}
