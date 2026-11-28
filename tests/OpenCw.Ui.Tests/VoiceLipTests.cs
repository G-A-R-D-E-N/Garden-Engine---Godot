using System;
using OpenCommonwealth.Services.Dialogue;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class VoiceLipTests
{
    [Fact]
    public void LineMsRoundsUpAndAddsTheTail()
    {
        Assert.Equal(1150, VoiceTiming.LineMs(1.0));
        Assert.Equal(1151, VoiceTiming.LineMs(1.0001));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void LineMsIsZeroWhenTheLengthIsUnknown(double seconds) => Assert.Equal(0, VoiceTiming.LineMs(seconds));

    [Fact]
    public void OpennessClampsAndSkipsNegativeAndModifierSlots()
    {
        Assert.Equal(1f, MouthShape.Openness([0.8f, 0.7f, 0f, 0f], 3));
        Assert.Equal(0.5f, MouthShape.Openness([0.5f, -0.25f, 0f], 3), 5);
        Assert.Equal(0f, MouthShape.Openness([0f, 0f, 0f, 1f, 1f], 3));
        Assert.Equal(0f, MouthShape.Openness([], 3));
    }

    [Fact]
    public void OpennessTreatsTheSilenceSlotOfA43PhonemeFileAsClosed()
    {
        var slots = new float[MouthShape.VisemeCount + 1];
        slots[MouthShape.RestSlot] = 0.9f;
        Assert.Equal(0f, MouthShape.Openness(slots, MouthShape.VisemeCount));
        slots[5] = 0.4f;
        Assert.Equal(0.4f, MouthShape.Openness(slots, MouthShape.VisemeCount), 5);
        Assert.Equal(1f, MouthShape.Openness(slots, MouthShape.VisemeCount + 1));
    }

    [Fact]
    public void MeasureReadsTheYExtent()
    {
        var mouth = JawMorph.Measure(new float[] { 1, 2, 3, 4, 6, 5 });
        Assert.NotNull(mouth);
        Assert.Equal(4f, mouth!.Value.CenterY);
        Assert.Equal(4f, mouth.Value.Height);
        Assert.Null(JawMorph.Measure(new float[] { 1, 2 }));
        Assert.Null(JawMorph.Measure(new float[] { 0, 5, 0, 1, 5, 1 }));
    }

    [Fact]
    public void WeightIsZeroAboveTheMouthFullBelowAndFadesAtTheNeck()
    {
        var mouth = new MouthBounds(10f, 2f);
        Assert.Equal(0f, JawMorph.Weight(13f, mouth));
        Assert.Equal(0f, JawMorph.Weight(10f + 1f, mouth));
        Assert.Equal(1f, JawMorph.Weight(10f - 2f, mouth));
        Assert.Equal(1f, JawMorph.Weight(10f - 5f, mouth));
        Assert.Equal(0f, JawMorph.Weight(10f - 10f, mouth));
        Assert.Equal(0f, JawMorph.Weight(10f - 20f, mouth));
        float mid = JawMorph.Weight(10f - 7.5f, mouth);
        Assert.InRange(mid, 0.01f, 0.99f);
    }

    [Fact]
    public void TargetLowersOnlyYBelowTheMouthAndKeepsTheLength()
    {
        var mouth = new MouthBounds(10f, 2f);
        float[] positions = [1, 20, 3, 4, 8, 6, 7, 10f - 4f, 9, 1, 100, 1];
        var target = JawMorph.Target(positions, mouth, 0.5f);
        Assert.Equal(positions.Length, target.Length);
        Assert.Equal(20f, target[1]);
        Assert.Equal(8f - 0.5f, target[4], 5);
        Assert.Equal(6f - 0.5f, target[7], 5);
        Assert.Equal(100f, target[10]);
        for (int i = 0; i < positions.Length; i += 3)
        {
            Assert.Equal(positions[i], target[i]);
            Assert.Equal(positions[i + 2], target[i + 2]);
        }
        Assert.Equal(1f, positions[0]);
        Assert.Equal(8f, positions[4]);
    }
}
