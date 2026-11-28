using OpenCommonwealth.Services;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WorldspaceCellRetryTests
{
    [Fact]
    public void Terrain_failed_cell_is_not_up_to_date_so_the_next_run_retries_it()
    {
        var wsDirAbs = TempDir();
        try
        {
            var cellsDirAbs = Path.Combine(wsDirAbs, WorldspaceCellRetry.CellsFolder);
            Directory.CreateDirectory(cellsDirAbs);
            File.WriteAllText(Path.Combine(cellsDirAbs, "cell_3_2.tscn"),
                "[gd_scene format=3]\n\n[node name=\"Cell\" type=\"Node3D\"]\n");
            Assert.True(WorldspaceCellRetry.IsUpToDate(wsDirAbs, 3, 2));

            var markerAbs = WorldspaceCellRetry.FailureMarkerPath(wsDirAbs, 3, 2);
            File.WriteAllText(markerAbs, "ResourceSaver.Save(terrain/land_3_2.res) returned 15");
            Assert.False(WorldspaceCellRetry.IsUpToDate(wsDirAbs, 3, 2));

            File.Delete(markerAbs);
            Assert.True(WorldspaceCellRetry.IsUpToDate(wsDirAbs, 3, 2));
        }
        finally
        {
            Directory.Delete(wsDirAbs, recursive: true);
        }
    }

    static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "opencw-worldspace-retry-" + Guid.NewGuid().ToString("N"));
}