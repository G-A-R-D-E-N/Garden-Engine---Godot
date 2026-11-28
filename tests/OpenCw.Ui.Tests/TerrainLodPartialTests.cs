using OpenCommonwealth.Services.Terrain;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class TerrainLodPartialTests
{
    private readonly List<string> _log = new();

    [Fact]
    public void NoUnreadCells_IsComplete_WithOrWithoutAPreviousStore()
    {
        Assert.True(TerrainLodPartial.CheckUnread(Array.Empty<(int, int)>(), hasPrevious: true, replacePartial: false, _log.Add));
        Assert.True(TerrainLodPartial.CheckUnread(Array.Empty<(int, int)>(), hasPrevious: false, replacePartial: false, _log.Add));
        Assert.Empty(_log);
    }

    [Fact]
    public void UnreadCells_WithAPreviousStore_Refuse()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            TerrainLodPartial.CheckUnread(new[] { (4, -1), (2, 3) }, hasPrevious: true, replacePartial: false, _log.Add));
        Assert.Contains("2 LAND cells", ex.Message);
        Assert.Contains("(4,-1), (2,3)", ex.Message);
        Assert.Contains(TerrainLodPartial.ReplaceFlag, ex.Message);
    }

    [Fact]
    public void UnreadCells_WithoutAPreviousStore_BakeButAreIncomplete()
    {
        Assert.False(TerrainLodPartial.CheckUnread(new[] { (2, 3) }, hasPrevious: false, replacePartial: false, _log.Add));
        Assert.Contains(_log, l => l.Contains("(2,3)"));
    }

    [Fact]
    public void UnreadCells_WithTheFlag_AreAccepted()
    {
        Assert.True(TerrainLodPartial.CheckUnread(new[] { (2, 3) }, hasPrevious: true, replacePartial: true, _log.Add));
        Assert.Contains(_log, l => l.Contains("(2,3)") && l.Contains(TerrainLodPartial.ReplaceFlag));
    }

    [Fact]
    public void ManyUnreadCells_ListTheFirstTen()
    {
        var cells = Enumerable.Range(0, 12).Select(x => (x, 0)).ToArray();
        var ex = Assert.Throws<InvalidDataException>(() =>
            TerrainLodPartial.CheckUnread(cells, hasPrevious: true, replacePartial: false, _log.Add));
        Assert.Contains("(9,0), ...", ex.Message);
        Assert.DoesNotContain("(10,0)", ex.Message);
    }

    [Fact]
    public void CheckKept_ThrowsOnlyWhenThePreviousStoreWouldLoseIt()
    {
        var failure = new IOException("archive read failed");
        var ex = Assert.Throws<InvalidDataException>(() =>
            TerrainLodPartial.CheckKept("colour pages", "read failed", previousHas: true, replacePartial: false, failure));
        Assert.Same(failure, ex.InnerException);
        Assert.Contains("colour pages (read failed)", ex.Message);
        Assert.Contains(TerrainLodPartial.ReplaceFlag, ex.Message);
        Assert.Null(Assert.Throws<InvalidDataException>(() =>
            TerrainLodPartial.CheckKept("colour pages", "none", previousHas: true, replacePartial: false)).InnerException);

        TerrainLodPartial.CheckKept("colour pages", "read failed", previousHas: true, replacePartial: true, failure);
        TerrainLodPartial.CheckKept("colour pages", "read failed", previousHas: false, replacePartial: false, failure);
    }
}
