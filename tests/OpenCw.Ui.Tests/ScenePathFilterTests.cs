using OpenCommonwealth;
using Xunit;

public class ScenePathFilterTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("res://worldspaces/commonwealth/persistent/pers_0_0.tscn", false)]
    [InlineData("res://worldspaces/commonwealth/cells/0_0.tscn", true)]
    [InlineData("res://worldspaces/commonwealth/cells/persistent_house.tscn", true)]
    public void Exterior_cells_exclude_persistent_bucket_scenes(string? path, bool expected)
    {
        Assert.Equal(expected, ScenePathFilter.IsExteriorCell(path));
    }
}
