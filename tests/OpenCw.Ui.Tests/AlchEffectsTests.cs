using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class AlchEffectsTests
{
    static readonly FormKey Heal = new("Fallout4.esm", 0x100), Rad = new("Fallout4.esm", 0x200);

    static MagicEffectEntry Entry(FormKey? effect, float magnitude = 1, uint duration = 0, bool conditional = false) =>
        new(effect, magnitude, 0, duration, conditional);

    [Fact]
    public void EffectsComeInRecordOrderWithMagnitudeDurationAndConditionalFlag()
    {
        var effects = RadicleAlchEffects.Map(new[] { Entry(Heal, 50), Entry(Rad, 2.5f, 60, true) })!;
        Assert.Equal(new[] { new ConsumableEffect(RecordKey.Format(Heal), 50, 0), new ConsumableEffect(RecordKey.Format(Rad), 2.5, 60, true) }, effects);
    }

    [Fact]
    public void ARecordWithNoEffectsIsAReadableEmptyList() => Assert.Empty(RadicleAlchEffects.Map(Array.Empty<MagicEffectEntry>())!);

    [Theory]
    [InlineData("nullEffect")]
    [InlineData("nanMagnitude")]
    [InlineData("infiniteMagnitude")]
    public void AnEffectTheMappingCannotReadMakesTheWholeListUnreadable(string shape)
    {
        var bad = shape switch
        {
            "nullEffect" => Entry(null),
            "nanMagnitude" => Entry(Rad, float.NaN),
            _ => Entry(Rad, float.PositiveInfinity),
        };
        Assert.Null(RadicleAlchEffects.Map(new[] { Entry(Heal), bad }));
    }

    [Fact]
    public void ADurationAboveIntMaxIsClamped() =>
        Assert.Equal(int.MaxValue, RadicleAlchEffects.Map(new[] { Entry(Heal, 1, uint.MaxValue) })![0].Duration);

    [Fact]
    public void ItemStatsNameEachEffectAndShowDurationOnlyWhenPositive()
    {
        var reader = new FakeRecordReader();
        reader.Add("000100:fallout4.esm", "MGEF", "HealEffect", "Restore Health");
        reader.Add("000200:fallout4.esm", "MGEF", "RadEffect");
        var stats = PlayerInventoryItems.EffectStats(new[]
        {
            new ConsumableEffect("000100:fallout4.esm", 50, 0),
            new ConsumableEffect("000200:fallout4.esm", 10, 30, true),
            new ConsumableEffect("000300:fallout4.esm", 1, 0),
        }, reader)!;
        Assert.Equal(new[] { new ItemStat("Restore Health", 50), new ItemStat("RadEffect (conditional)", 10),
                          new ItemStat("RadEffect (conditional) duration", 30), new ItemStat("000300:fallout4.esm", 1) }, stats);
        Assert.Null(PlayerInventoryItems.EffectStats(null, reader));
        Assert.Null(PlayerInventoryItems.EffectStats(Array.Empty<ConsumableEffect>(), reader));
    }

    [Fact]
    public void OnlyAlchItemsGetEffectStatsAndNothingBecomesUsable()
    {
        const string Stimpak = "000001:fallout4.esm", Junk = "000002:fallout4.esm";
        var reader = new FakeRecordReader();
        reader.Add(Stimpak, "ALCH", name: "Stimpak");
        reader.Add(Junk, "MISC", name: "Junk");
        reader.Add("000100:fallout4.esm", "MGEF", name: "Restore Health");
        var items = new PlayerItems();
        items.Add(Stimpak, 1);
        items.Add(Junk, 1);
        var built = PlayerInventoryItems.Build(items.Stacks, new RecordItems(reader), new PlayerEquipment(), reader,
                                               effects: _ => new[] { new ConsumableEffect("000100:fallout4.esm", 50, 0) });
        Assert.Equal(new[] { new ItemStat("Restore Health", 50) }, built.Single(i => i.Id == Stimpak).StatList);
        Assert.Empty(built.Single(i => i.Id == Junk).StatList);
        Assert.False(built.Single(i => i.Id == Stimpak).Usable);
    }
}
