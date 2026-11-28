using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class BrowseProgressionTests
{
    private static IReadOnlyList<SpecialEntry> Sheet(params double[] values) =>
        PlayerSpecialRows.Ids.Select((id, i) => new SpecialEntry(id, id, values[i])).ToArray();

    [Fact]
    public void Level_follows_experience_and_grants_spendable_perk_points()
    {
        PlayerExperience.Current.Clear();
        PlayerPerks.Current.Clear();
        using var progression = new BrowseProgression();
        PlayerExperience.Current.Add(50_000);

        Assert.Equal(35, PlayerExperience.Current.Level);
        Assert.Equal(35, progression.Snapshot.Level);
        Assert.Equal(34, progression.Snapshot.PerkPoints);
        Assert.True(progression.Snapshot.Chart.Entries.Any(e => e.Kind == PerkChartKind.Attribute && e.Available));
        Assert.True(progression.Snapshot.IsValid);
        Assert.Equal(7, progression.Snapshot.Chart.Entries.Count(e => e.Kind == PerkChartKind.Attribute));
        Assert.True(progression.Snapshot.Chart.Revision > 0);
        PlayerExperience.Current.Clear();
    }

    [Fact]
    public void Attributes_come_from_the_stored_sheet_else_default_to_five()
    {
        using var stored = new BrowseProgression(() => Sheet(1, 2, 3, 4, 5, 6, 10));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 10 }, stored.Snapshot.Chart.Attributes);
        using var partial = new BrowseProgression(() => Sheet(1, 2, 3, 4, 5, 6, 7).Take(3).ToArray());
        Assert.All(partial.Snapshot.Chart.Attributes, a => Assert.Equal(5, a));
    }

    [Fact]
    public void Dispose_stops_listening_to_experience()
    {
        var progression = new BrowseProgression();
        int raised = 0;
        progression.Changed += () => raised++;
        progression.Dispose();
        PlayerExperience.Current.Add(1);
        PlayerPerks.Current.Add("01000800");
        Assert.Equal(0, raised);
        PlayerExperience.Current.Clear();
        PlayerPerks.Current.Clear();
    }

    [Fact]
    public void Owned_perks_refresh_the_browse_chart()
    {
        PlayerPerks.Current.Clear();
        using var progression = new BrowseProgression();
        Assert.Empty(progression.Snapshot.Chart.Entries.Where(e => e.Kind == PerkChartKind.Perk));
        PlayerPerks.Current.Add("01000800");
        Assert.Single(progression.Snapshot.Chart.Entries.Where(e => e.Kind == PerkChartKind.Perk));
        PlayerPerks.Current.Clear();
    }

    [Fact]
    public void TryChoose_spends_attribute_points_when_the_player_has_levels()
    {
        PlayerExperience.Current.Clear();
        using var progression = new BrowseProgression();
        Assert.Equal(ChooseResult.NoPoints, progression.TryChoose("attr0", 6));
        PlayerExperience.Current.Add(50_000);
        Assert.Equal(34, progression.Snapshot.PerkPoints);
        Assert.Equal(ChooseResult.Ok, progression.TryChoose("attr0", 6));
        Assert.Equal(6, progression.Snapshot.Chart.Attributes[0]);
        Assert.Equal(33, progression.Snapshot.PerkPoints);
        PlayerExperience.Current.Clear();
    }

    [Fact]
    public void Spent_points_survive_a_save_round_trip_and_are_not_regranted()
    {
        PlayerExperience.Current.Clear();
        PlayerExperience.Current.Add(50_000);
        using (var first = new BrowseProgression())
        {
            var points = first.Snapshot.PerkPoints;
            Assert.Equal(ChooseResult.Ok, first.TryChoose("attr0", 6));
            Assert.Equal(points - 1, first.Snapshot.PerkPoints);
        }
        var saved = PlayerExperience.Current.Sections();
        PlayerExperience.Current.Clear();
        PlayerExperience.Current.Apply(saved);
        using var reloaded = new BrowseProgression();
        Assert.Equal(6, reloaded.Snapshot.Chart.Attributes[0]);
        Assert.Equal(PlayerExperience.Current.Level - 2, reloaded.Snapshot.PerkPoints);
        PlayerExperience.Current.Clear();
    }

    [Fact]
    public void Applying_a_save_with_the_same_total_but_new_spends_refreshes_a_live_chart()
    {
        PlayerExperience.Current.Clear();
        PlayerExperience.Current.Add(50_000);
        using var progression = new BrowseProgression(() => Sheet(5, 5, 5, 5, 5, 5, 5));
        Assert.Equal(5, progression.Snapshot.Chart.Attributes[0]);
        var save = System.Text.Encoding.UTF8.GetBytes("{\"total\":50000,\"spent\":[2,0,0,0,0,0,1]}");
        PlayerExperience.Current.Apply(new Dictionary<string, byte[]> { [PlayerExperience.SectionName] = save });
        Assert.Equal(7, progression.Snapshot.Chart.Attributes[0]);
        Assert.Equal(6, progression.Snapshot.Chart.Attributes[6]);
        Assert.Equal(31, progression.Snapshot.PerkPoints);
        PlayerExperience.Current.Clear();
    }

    [Fact]
    public void Pipboy_sheet_and_level_up_chart_agree_after_a_reload_without_double_counting()
    {
        PlayerExperience.Current.Clear();
        PlayerExperience.Current.Add(50_000);
        var stored = Sheet(5, 5, 5, 5, 5, 5, 5);
        using (var first = new BrowseProgression(() => stored))
        {
            Assert.Equal(ChooseResult.Ok, first.TryChoose("attr0", 6));
            Assert.Equal(ChooseResult.Ok, first.TryChoose("attr6", 6));
        }
        var saved = PlayerExperience.Current.Sections();
        PlayerExperience.Current.Clear();
        PlayerExperience.Current.Apply(saved);
        using var reloaded = new BrowseProgression(() => stored);
        var shown = PlayerSpecialRows.WithSpent(stored, PlayerExperience.Current.Spent);
        Assert.Equal(new[] { 6, 5, 5, 5, 5, 5, 6 }, shown.Select(r => (int)r.Value));
        Assert.Equal(shown.Select(r => (int)r.Value), reloaded.Snapshot.Chart.Attributes);
        Assert.Equal(5, (int)stored[0].Value);
        PlayerExperience.Current.Clear();
    }

    [Fact]
    public void WithSpent_caps_at_ten_and_leaves_an_incomplete_sheet_alone()
    {
        var spent = new[] { 9, 0, 0, 0, 0, 0, 0 };
        Assert.Equal(10, PlayerSpecialRows.WithSpent(Sheet(5, 5, 5, 5, 5, 5, 5), spent)[0].Value);
        var partial = Sheet(5, 5, 5, 5, 5, 5, 5).Take(3).ToArray();
        Assert.Same(partial, PlayerSpecialRows.WithSpent(partial, spent));
    }

    [Fact]
    public void Spent_is_clamped_on_load_and_a_missing_section_clears_it()
    {
        PlayerExperience.Current.Clear();
        var bad = System.Text.Encoding.UTF8.GetBytes("{\"total\":10,\"spent\":[999,-4,1]}");
        PlayerExperience.Current.Apply(new Dictionary<string, byte[]> { [PlayerExperience.SectionName] = bad });
        Assert.Equal(new[] { 255, 0, 1, 0, 0, 0, 0 }, PlayerExperience.Current.Spent);
        PlayerExperience.Current.Apply(new Dictionary<string, byte[]>());
        Assert.All(PlayerExperience.Current.Spent, v => Assert.Equal(0, v));
        PlayerExperience.Current.Clear();
    }
}
