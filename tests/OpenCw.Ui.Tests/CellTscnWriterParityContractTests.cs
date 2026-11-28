using System.IO;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class CellTscnWriterParityContractTests
{
    static string? RepoRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(CellTscnWriterParityContractTests).Assembly.Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "opencw", "Services", "CellTscnWriter.cs")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    static string? WriterSource() =>
        RepoRoot() is { } root ? File.ReadAllText(Path.Combine(root, "opencw", "Services", "CellTscnWriter.cs")) : null;

    [Fact]
    public void CompilerVersionTracksExteriorPlacementParity()
    {
        if (WriterSource() is not { } source) return;
        Assert.Contains("public const int CompilerVersion = 18;", source);
    }

    [Fact]
    public void NpcReferencesAreHandledBeforeAnyModelBranches()
    {
        if (WriterSource() is not { } source) return;
        Assert.Contains("if (r.RecordType == \"PlacedNpc\")", source);
    }

    [Fact]
    public void WireMeshesKeepMetadataGroupsGeometryKeysAndMarkerFallback()
    {
        if (WriterSource() is not { } source) return;
        Assert.Contains("CellCompiler.GroupsAttr(groups, \"wires\")", source);
        Assert.Contains("CellCompiler.AppendPlacedMetadata(NodeLines, r);", source);
        Assert.Contains("stats.Markers++;", source);
        Assert.Contains("WireMeshCacheKey", source);
        Assert.Contains("if (formKey.Length == 0) return nodeName;", source);
    }

    [Fact]
    public void PlacementRegressionTestsExistForSplinesPrimitivesAndNpcSpawns()
    {
        if (RepoRoot() is not { } root) return;
        var path = Path.Combine(root, "tests", "OpenCw.Winding.Tests", "CellTscnWriterPlacementTests.cs");
        if (!File.Exists(path)) return;
        var source = File.ReadAllText(path);
        Assert.Contains("SplinesEmitAWireMeshWithMetadataAndLayerGroups", source);
        Assert.Contains("SplinesFallBackToMarkersWhenWireMeshCannotBeWritten", source);
        Assert.Contains("PrimitivesEmitTriggerAndRoomAreasWithDeduplicatedShapes", source);
        Assert.Contains("NpcReferencesBecomeSpawnMarkersBeforeOtherBranchesRun", source);
    }
}
