using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PipBoyStatusTests
{
    sealed class FakeStatus : IPlayerStatusSource
    {
        public string? PlayerName { get; set; } = "Sample Survivor";

        public double? Health { get; set; } = 40;

        public double? MaxHealth { get; set; } = 100;

        public double? PendingHealthGain { get; set; }

        public double? Radiation { get; set; }

        public IReadOnlyList<DamageTotal>? DamageTotals { get; set; }

        public IReadOnlyList<DamageTotal>? ResistTotals { get; set; }

        public AidSupply? Stimpak { get; set; }

        public AidSupply? RadAway { get; set; }

        public List<EffectSource> EffectList { get; } = new();

        public List<SpecialEntry> SpecialList { get; } = new();

        public List<OwnedPerk> PerkList { get; } = new();

        public Dictionary<LimbKind, LimbCondition> Limbs { get; } = new();

        public bool ReadOnly { get; set; }

        public IReadOnlyList<EffectSource> Effects => EffectList;

        public IReadOnlyList<SpecialEntry> Special => SpecialList;

        public IReadOnlyList<OwnedPerk> Perks => PerkList;

        public LimbCondition? Limb(LimbKind limb) => Limbs.GetValueOrDefault(limb);
    }

    static FakeStatus WithSpecial()
    {
        var source = new FakeStatus();
        source.SpecialList.Add(new SpecialEntry("s", "Strength", 6, 1, "Strength text"));
        source.SpecialList.Add(new SpecialEntry("p", "Perception", 5.5, -2, "Perception text"));
        source.SpecialList.Add(new SpecialEntry("e", "Endurance", 4));
        return source;
    }

    static FakeStatus WithPerks()
    {
        var source = new FakeStatus();
        source.PerkList.Add(new OwnedPerk("z", "Zeta", 1, new[] { "z1" }));
        source.PerkList.Add(new OwnedPerk("a", "Alpha", 2, new[] { "a1", "a2", "a3" }));
        source.PerkList.Add(new OwnedPerk("b", "beta", 1, new[] { "b1", "b2" }));
        return source;
    }

    [Fact]
    public void Sub_tab_clamps_without_wrap_and_resets_selection_rank_and_effect_panel()
    {
        var source = WithPerks();
        source.EffectList.Add(new EffectSource(1, "Chem", new[] { new EffectLine("Speed", 1) }));
        var model = new PlayerStatusModel(source);
        Assert.True(model.ToggleEffects());

        model.SetSubTab(-5);
        Assert.Equal(StatusSubTab.Status, model.SubTab);
        Assert.True(model.EffectsShown);

        model.SetSubTab(9);
        Assert.Equal(StatusSubTab.Perks, model.SubTab);
        Assert.False(model.EffectsShown);
        Assert.Equal("Alpha (2)", model.View.Current!.Label);

        Assert.True(model.NextRank());
        model.View.SelectVisible(1);
        model.SetSubTab(StatusSubTab.Special);
        model.SetSubTab(StatusSubTab.Perks);
        Assert.Equal(0, model.View.CursorIndex);
        Assert.Equal(2, model.ViewedRank);
    }

    [Fact]
    public void Condition_hides_missing_limbs_and_marks_crippled_ones()
    {
        var source = new FakeStatus { Health = -3, MaxHealth = 100 };
        source.Limbs[LimbKind.Head] = new LimbCondition(0.75);
        source.Limbs[LimbKind.LeftLeg] = new LimbCondition(0, true);
        source.Limbs[LimbKind.RightLeg] = new LimbCondition(0);
        var model = new PlayerStatusModel(source);

        var rows = model.Condition;

        Assert.Equal(new[] { "Head", "Left leg", "Right leg", "Torso" }, rows.Select(row => row.Label));
        Assert.Equal("75%", rows[0].Text);
        Assert.Equal("CRIPPLED", rows[1].Text);
        Assert.True(rows[1].Crippled);
        Assert.Equal("0%", rows[2].Text);
        Assert.False(rows[2].Crippled);
        Assert.Equal("0/100", rows[3].Text);
    }

    [Fact]
    public void Torso_shows_a_lighter_pending_segment_only_while_a_gain_is_pending_and_never_past_the_maximum()
    {
        var source = new FakeStatus { Health = 40, MaxHealth = 100 };
        var model = new PlayerStatusModel(source);
        var torso = model.Condition.Single();
        Assert.Equal("40/100", torso.Text);
        Assert.Equal(0.4, torso.Fraction, 6);
        Assert.Equal(0.4, torso.PendingFraction, 6);

        source.PendingHealthGain = 30;
        Assert.Equal(0.7, model.Condition.Single().PendingFraction, 6);

        source.PendingHealthGain = 500;
        Assert.Equal(1.0, model.Condition.Single().PendingFraction, 6);

        source.Health = null;
        Assert.Empty(model.Condition);
    }

    [Fact]
    public void Radiation_row_appears_only_above_zero()
    {
        var source = new FakeStatus { Radiation = 0 };
        var model = new PlayerStatusModel(source);
        Assert.Null(model.RadiationFraction);
        Assert.DoesNotContain(model.View.Visible, row => row.Key == "radiation");

        source.Radiation = 0.25;
        model.Refresh();
        Assert.Equal(0.25, model.RadiationFraction!.Value);
        Assert.Contains(model.View.Visible, row => row is { Key: "radiation", Value: "25%" });
    }

    [Fact]
    public void Damage_and_resist_lines_omit_zero_kinds_and_fall_back_to_physical_zero()
    {
        var source = new FakeStatus
        {
            DamageTotals = new[] { new DamageTotal("ballistic", 12.5), new DamageTotal("energy", 0), new DamageTotal("fire", 3) },
            ResistTotals = new[] { new DamageTotal("ballistic", 0), new DamageTotal("energy", 0) },
        };
        var model = new PlayerStatusModel(source);

        Assert.Equal("BALLISTIC 12.5  FIRE 3", model.DamageLine);
        Assert.Equal("PHYSICAL 0", model.ResistLine);

        source.DamageTotals = null;
        source.ResistTotals = null;
        Assert.Null(model.DamageLine);
        Assert.Null(model.ResistLine);
    }

    [Fact]
    public void Aid_actions_need_a_positive_count_and_a_player_who_can_act()
    {
        var source = new FakeStatus { Stimpak = new AidSupply("Stimpak", 2), RadAway = new AidSupply("RadAway", 0) };
        var model = new PlayerStatusModel(source);
        var requested = new List<AidKind>();
        model.AidRequested += requested.Add;

        Assert.Equal(new[] { "USE STIMPAK (2)", "USE RADAWAY (0)" }, model.Aid.Select(action => action.Label));
        Assert.Equal(new[] { true, false }, model.Aid.Select(action => action.Enabled));

        Assert.True(model.RequestAid(AidKind.Stimpak));
        Assert.False(model.RequestAid(AidKind.RadAway));
        Assert.Equal(new[] { AidKind.Stimpak }, requested);

        source.ReadOnly = true;
        Assert.False(model.RequestAid(AidKind.Stimpak));
        Assert.All(model.Aid, action => Assert.False(action.Enabled));
        Assert.Single(requested);
    }

    [Fact]
    public void Aid_row_accept_requests_aid_through_the_list_view()
    {
        var source = new FakeStatus { Stimpak = new AidSupply("Stimpak", 1) };
        var model = new PlayerStatusModel(source);
        var view = new StatusListView(model);
        var requested = new List<AidKind>();
        model.AidRequested += requested.Add;

        var index = model.View.Visible.ToList().FindIndex(row => row.Key == "aid:stimpak");
        Assert.True(view.Activate(index));

        Assert.Equal(new[] { AidKind.Stimpak }, requested);
        Assert.Equal("Use", view.Hints.First(hint => hint.Action == MenuInputAction.Accept).Label);
    }

    [Fact]
    public void Effects_merge_by_name_group_ascending_and_format_values()
    {
        var source = new FakeStatus();
        source.EffectList.Add(new EffectSource(2, "Late", new[] { new EffectLine("Glowing", 0, CustomDescription: true) }));
        source.EffectList.Add(new EffectSource(1, "Chem", new[]
        {
            new EffectLine("Strength", 1),
            new EffectLine("Strength", 2.256, Duration: 30),
            new EffectLine("Speed", 10, true),
            new EffectLine("Slow", -4.5),
        }));
        var model = new PlayerStatusModel(source);

        var groups = model.EffectView;

        Assert.Equal(new[] { 1, 2 }, groups.Select(group => group.Group));
        Assert.Equal("CHEM", groups[0].Entries[0].Source);
        Assert.Equal(new[] { "STRENGTH +3.26", "SPEED +10%", "SLOW -4.5" }, groups[0].Entries[0].Lines);
        Assert.Equal(new[] { "GLOWING" }, groups[1].Entries[0].Lines);
        Assert.Equal(new[] { "CHEM:", "STRENGTH +3.26", "SPEED +10%", "SLOW -4.5", "LATE:", "GLOWING" }, model.EffectLines);
    }

    [Fact]
    public void Effect_toggle_needs_effects_and_the_panel_closes_when_they_vanish()
    {
        var source = new FakeStatus();
        var model = new PlayerStatusModel(source);
        Assert.False(model.HasEffects);
        Assert.False(model.ToggleEffects());

        source.EffectList.Add(new EffectSource(1, "Chem", new[] { new EffectLine("Speed", 1) }));
        model.Refresh();
        Assert.True(model.ToggleEffects());
        Assert.True(model.EffectsShown);
        Assert.True(model.Back());
        Assert.False(model.EffectsShown);
        Assert.False(model.Back());

        Assert.True(model.ToggleEffects());
        source.EffectList.Clear();
        model.Refresh();
        Assert.False(model.EffectsShown);
        Assert.False(model.Back());
    }

    [Fact]
    public void Effect_scroll_is_clamped_at_both_ends_and_only_moves_while_the_panel_is_shown()
    {
        var source = new FakeStatus();
        source.EffectList.Add(new EffectSource(1, "Chem", Enumerable.Range(0, 19).Select(i => new EffectLine("Line" + i, 1)).ToArray()));
        var model = new PlayerStatusModel(source) { EffectViewRows = 5 };
        Assert.False(model.ScrollEffects(1));

        model.ToggleEffects();
        Assert.Equal(20, model.EffectLines.Count);
        Assert.True(model.ScrollEffects(100));
        Assert.Equal(15, model.EffectScroll);
        Assert.False(model.ScrollEffects(1));
        Assert.True(model.ScrollEffects(-100));
        Assert.Equal(0, model.EffectScroll);
        Assert.False(model.ScrollEffects(-1));
    }

    [Fact]
    public void Special_rows_keep_source_order_and_mark_modifiers()
    {
        var model = new PlayerStatusModel(WithSpecial());
        model.SetSubTab(StatusSubTab.Special);

        Assert.Equal(new[] { "Strength", "Perception", "Endurance" }, model.View.Visible.Select(row => row.Label));
        Assert.Equal(new[] { "+6", "-5.5", "4" }, model.View.Visible.Select(row => row.Value));
        Assert.Equal("Strength text", model.SelectedDescription);

        model.View.Move(OpenCommonwealth.Services.Ui.Lists.CursorMove.Down);
        Assert.Equal("Perception text", model.SelectedDescription);
        model.View.Move(OpenCommonwealth.Services.Ui.Lists.CursorMove.Down);
        model.View.Move(OpenCommonwealth.Services.Ui.Lists.CursorMove.Down);
        Assert.Equal("Endurance", model.View.Current!.Label);
    }

    [Fact]
    public void Perks_sort_ordinally_and_show_rank_only_above_one()
    {
        var model = new PlayerStatusModel(WithPerks());
        model.SetSubTab(StatusSubTab.Perks);

        Assert.Equal(new[] { "Alpha (2)", "Zeta", "beta" }, model.View.Visible.Select(row => row.Label));
        Assert.Equal(string.Empty, model.EmptyMessage);
    }

    [Fact]
    public void Perk_rank_preview_is_bounded_marks_unowned_ranks_and_resets_with_the_selection()
    {
        var model = new PlayerStatusModel(WithPerks());
        model.SetSubTab(StatusSubTab.Perks);

        Assert.Equal(2, model.ViewedRank);
        Assert.Equal("RANK 2 / 3", model.RankLine);
        Assert.Equal("a2", model.SelectedDescription);
        Assert.True(model.CanPrevRank);
        Assert.True(model.CanNextRank);

        Assert.True(model.NextRank());
        Assert.Equal("RANK 3 / 3 (not owned)", model.RankLine);
        Assert.Equal("a3", model.SelectedDescription);
        Assert.False(model.CanNextRank);
        Assert.False(model.NextRank());

        Assert.True(model.PrevRank());
        Assert.True(model.PrevRank());
        Assert.Equal(1, model.ViewedRank);
        Assert.False(model.CanPrevRank);
        Assert.False(model.PrevRank());
        Assert.Equal("RANK 1 / 3", model.RankLine);

        model.View.SelectVisible(1);
        Assert.Equal(string.Empty, model.RankLine);
        Assert.False(model.CanPrevRank);
        Assert.False(model.CanNextRank);
        model.View.SelectVisible(0);
        Assert.Equal(2, model.ViewedRank);
    }

    [Fact]
    public void Perk_list_without_perks_shows_the_empty_message_and_no_rank_actions()
    {
        var model = new PlayerStatusModel(new FakeStatus());
        model.SetSubTab(StatusSubTab.Perks);

        Assert.Equal("NO PERKS", model.EmptyMessage);
        Assert.Null(model.SelectedPerk);
        Assert.False(model.CanNextRank);
        Assert.False(model.PrevRank());
    }

    [Fact]
    public void Refresh_keeps_the_selected_row_by_key_and_resets_the_viewed_rank()
    {
        var source = WithPerks();
        var model = new PlayerStatusModel(source);
        model.SetSubTab(StatusSubTab.Perks);
        model.View.SelectVisible(2);
        Assert.Equal("beta", model.SelectedPerk!.Name);
        model.NextRank();

        source.PerkList.Add(new OwnedPerk("c", "Aardvark", 1, new[] { "c1" }));
        model.Refresh();

        Assert.Equal("beta", model.SelectedPerk!.Name);
        Assert.Equal(1, model.ViewedRank);
    }

    [Fact]
    public void Select_by_key_switches_sub_tab_and_a_missing_key_changes_nothing()
    {
        var source = WithPerks();
        source.SpecialList.Add(new SpecialEntry("s", "Strength", 6));
        var model = new PlayerStatusModel(source);

        Assert.True(model.Select("perk:b"));
        Assert.Equal(StatusSubTab.Perks, model.SubTab);
        Assert.Equal("perk:b", model.CurrentKey);

        Assert.False(model.Select("special:missing"));
        Assert.Equal(StatusSubTab.Perks, model.SubTab);
        Assert.Equal("perk:b", model.CurrentKey);

        Assert.True(model.Select("special:s"));
        Assert.Equal(StatusSubTab.Special, model.SubTab);
    }

    [Fact]
    public void Search_finds_special_attributes_and_perks_by_text_and_pip_boy_jumps_to_them()
    {
        var source = WithPerks();
        source.SpecialList.Add(new SpecialEntry("s", "Strength", 6, 0, "Lifts heavy loads"));
        var model = new PlayerStatusModel(source);
        var pipBoy = new PipBoyModel { InstantTransitions = true };
        pipBoy.Register(model);
        pipBoy.Open(PipBoyTab.Inventory);

        pipBoy.Search("heavy");
        Assert.Equal(new[] { "special:s" }, pipBoy.SearchHits.Select(hit => hit.Key));

        Assert.True(pipBoy.JumpToHit(0));
        Assert.Equal(PipBoyTab.Stats, pipBoy.Tab);
        Assert.Equal(StatusSubTab.Special, model.SubTab);

        pipBoy.Search("a3");
        Assert.Equal(new[] { "perk:a" }, pipBoy.SearchHits.Select(hit => hit.Key));
    }

    [Fact]
    public void List_view_tabs_follow_the_sub_tab_and_hints_offer_rank_and_effect_actions()
    {
        var source = WithPerks();
        source.EffectList.Add(new EffectSource(1, "Chem", new[] { new EffectLine("Speed", 1) }));
        var model = new PlayerStatusModel(source);
        var view = new StatusListView(model);

        Assert.Equal(new[] { "STATUS", "SPECIAL", "PERKS" }, view.Tabs);
        Assert.Contains(view.Hints, hint => hint is { Action: MenuInputAction.Favourite, Label: "Show effects" });

        view.SelectTab(2);
        Assert.Equal(2, view.TabIndex);
        Assert.Contains(view.Hints, hint => hint is { Action: MenuInputAction.Favourite, Label: "Next rank" });
        Assert.Contains(view.Hints, hint => hint is { Action: MenuInputAction.Compare, Label: "Previous rank" });

        Assert.True(view.Handle(MenuInputAction.Favourite));
        Assert.Equal(3, model.ViewedRank);
        Assert.DoesNotContain(view.Hints, hint => hint.Action == MenuInputAction.Favourite);
        Assert.True(view.PrevRank());
        Assert.Equal(2, model.ViewedRank);
    }

    [Fact]
    public void Disposing_the_list_view_stops_it_following_the_model()
    {
        var model = new PlayerStatusModel(new FakeStatus());
        var view = new StatusListView(model);
        var raised = 0;
        view.Changed += () => raised++;

        model.Refresh();
        Assert.Equal(1, raised);

        view.Dispose();
        model.Refresh();

        Assert.Equal(1, raised);
    }
}
