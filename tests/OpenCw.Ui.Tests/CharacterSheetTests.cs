using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CharacterSheetTests
{
    static readonly GenericCharacterData Data = new();

    static Dictionary<string, double> BaseInputs() => Data.Stats.ToDictionary(s => s.Id, s => (double)s.Min);

    static CharacterSheetModel Make(params DerivedStat[] extra) =>
        new(Data.DerivedStats.Concat(extra).ToArray(), BaseInputs());

    [Fact]
    public void Without_a_pending_change_every_row_shows_the_same_value_before_and_after()
    {
        var sheet = Make();

        Assert.Equal(2, sheet.Rows.Count);
        Assert.All(sheet.Rows, row => Assert.Equal(row.Current, row.After));
        Assert.Equal(2, sheet.Rows[0].Current);
        Assert.Equal(2, sheet.Rows[1].Current);
        Assert.All(sheet.Rows, row => Assert.Equal(SheetChange.None, row.Change));
    }

    [Fact]
    public void A_pending_input_change_shows_current_to_after_only_on_the_rows_it_affects()
    {
        var sheet = Make();

        sheet.SetPending(new Dictionary<string, double> { ["s1"] = 4 });

        var row = Assert.Single(sheet.Rows, r => r.Changed);
        Assert.Equal("d1", row.Id);
        Assert.Equal("Total of 1 and 2", row.Label);
        Assert.Equal(2, row.Current);
        Assert.Equal(5, row.After);
        Assert.Equal(3, row.Delta);
        Assert.Equal(SheetChange.Better, row.Change);
        Assert.Equal(SheetChange.None, sheet.Rows[1].Change);
    }

    [Fact]
    public void A_stat_where_lower_is_better_reports_a_rise_as_worse()
    {
        var sheet = Make(new DerivedStat("w", "Load", v => v["s1"] * 3, HigherIsBetter: false));

        sheet.SetPending(new Dictionary<string, double> { ["s1"] = 2 });

        var row = sheet.Rows.Single(r => r.Id == "w");
        Assert.Equal(3, row.Current);
        Assert.Equal(6, row.After);
        Assert.Equal(SheetChange.Worse, row.Change);

        sheet.SetPending(new Dictionary<string, double> { ["s1"] = 1 });
        Assert.Equal(SheetChange.None, sheet.Rows.Single(r => r.Id == "w").Change);
    }

    [Fact]
    public void Preview_can_be_fed_straight_from_a_stat_allocation()
    {
        var allocation = new StatAllocationModel(Data.Stats, Data.StatPoints, true, Data.StatPresets);
        var sheet = Make();

        allocation.ApplyPreset("Even");
        sheet.SetPending(allocation.Values);

        Assert.Equal(2, sheet.Rows.Count(r => r.Changed));
        Assert.Equal(2, sheet.Rows[0].Current);
        Assert.Equal(4, sheet.Rows[0].After);
        Assert.Equal(2, sheet.Rows[1].Current);
        Assert.Equal(4, sheet.Rows[1].After);

        allocation.Undo();
        sheet.SetPending(allocation.Values);
        Assert.DoesNotContain(sheet.Rows, r => r.Changed);
    }

    [Fact]
    public void Changed_fires_on_every_pending_change()
    {
        var sheet = Make();
        var count = 0;
        sheet.Changed += () => count++;

        sheet.SetPending(new Dictionary<string, double> { ["s1"] = 3 });
        sheet.SetPending(new Dictionary<string, double> { ["s1"] = 4 });
        Assert.Equal(2, count);
    }

    [Fact]
    public void Duplicate_derived_ids_are_rejected()
    {
        var twice = new[] { Data.DerivedStats[0], Data.DerivedStats[0] };

        Assert.Throws<ArgumentException>(() => new CharacterSheetModel(twice, BaseInputs()));
    }
}
