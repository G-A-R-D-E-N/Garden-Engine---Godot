using System.Collections.Generic;
using System.Numerics;
using System.Text;
using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests;

public class HazardSaveTests
{
    static HazardInfo Hazard(float interval = 0.5f, float lifetime = 0f) => new(4f, lifetime, interval, 0, 16, null);

    [Fact]
    public void AHazardAddedWithAnAgeDoesNotTickForTheTimeItAlreadyLived()
    {
        var schedule = new HazardSchedule();
        var id = schedule.Add(Hazard(0.5f), 2.0)!.Value;

        Assert.Equal(2.0, schedule.AgeOf(id));
        Assert.Empty(schedule.Advance(0.2));
        Assert.Equal(new HazardTick(id, 1), Assert.Single(schedule.Advance(0.3)));
    }

    [Fact]
    public void AnAgeAtOrPastTheLifetimeIsRefused()
    {
        var schedule = new HazardSchedule();

        Assert.Null(schedule.Add(Hazard(0.5f, 10f), 10.0));
        Assert.Null(schedule.Add(Hazard(0.5f, 10f), -1.0));
        Assert.Null(schedule.Add(Hazard(0.5f, 10f), double.NaN));
        Assert.NotNull(schedule.Add(Hazard(0.5f, 10f), 9.5));
    }

    [Fact]
    public void ARestoredHazardExpiresAtItsOriginalEnd()
    {
        var schedule = new HazardSchedule();
        schedule.Add(Hazard(1f, 10f), 9.0);

        schedule.Advance(0.5);
        Assert.Equal(1, schedule.Count);
        schedule.Advance(0.6);
        Assert.Equal(0, schedule.Count);
    }

    [Fact]
    public void HazardRowsRoundTripThroughTheSection()
    {
        var rows = new List<HazardRow>
        {
            new("hazard-3", "000800:Fallout4.esm", new Vector3(1.5f, -2f, 30f), 4.25, true),
            new("hazard-9", "000801:Fallout4.esm", new Vector3(0f, 0f, 0f), 0, false),
        };

        var read = HazardSave.Read(HazardSave.Sections(rows))!;

        Assert.Equal(rows, read);
    }

    [Fact]
    public void NoHazardsWriteNoSectionAndASaveWithoutOneReadsAsNull()
    {
        Assert.Empty(HazardSave.Sections(new List<HazardRow>()));
        Assert.Null(HazardSave.Read(new Dictionary<string, byte[]>()));
    }

    [Theory]
    [InlineData("{\"version\":2,\"hazards\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"hazards\":[{\"token\":\"t\",\"base\":\"\",\"x\":0,\"y\":0,\"z\":0,\"age\":0,\"enabled\":true}]}")]
    [InlineData("{\"version\":1,\"hazards\":[{\"token\":\"t\",\"base\":\"b\",\"x\":0,\"y\":0,\"z\":0,\"age\":-1,\"enabled\":true}]}")]
    [InlineData("{\"version\":1,\"hazards\":[{\"token\":\"t\",\"base\":\"b\",\"x\":\"a\",\"y\":0,\"z\":0,\"age\":0,\"enabled\":true}]}")]
    [InlineData("{\"version\":1,\"hazards\":[{\"token\":\"t\",\"base\":\"b\",\"x\":0,\"y\":0,\"z\":0,\"age\":0}]}")]
    [InlineData("not json")]
    public void AMalformedSectionIsRefused(string json)
    {
        var sections = new Dictionary<string, byte[]> { [HazardSave.SectionName] = Encoding.UTF8.GetBytes(json) };

        Assert.Throws<System.FormatException>(() => HazardSave.Read(sections));
    }
}
