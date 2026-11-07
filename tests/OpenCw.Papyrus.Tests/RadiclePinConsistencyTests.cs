using System;
using System.IO;
using System.Text.RegularExpressions;
using OpenCommonwealth.Services.Papyrus;
using Xunit;

namespace OpenCw.Papyrus.Tests;

/// <summary>
/// One Radicle revision, one pin. The revision the engine builds and takes its managed binding from and
/// the revision the runtime records must be the same commit, or the runtime describes a Radicle that is
/// not the one it was built against.
/// </summary>
public class RadiclePinConsistencyTests
{
    [Fact]
    public void TheEnginePinIsThePinTheRuntimeRecords()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);

        var cmake = Path.Combine(root!, "opencw", "native", "radicle", "CMakeLists.txt");
        Assert.True(File.Exists(cmake), $"expected the engine's Radicle pin at {cmake}");

        var match = Regex.Match(File.ReadAllText(cmake), @"set\(RADICLE_COMMIT\s+([0-9a-f]{40})\)");
        Assert.True(match.Success, "the engine's CMakeLists.txt does not declare RADICLE_COMMIT");

        Assert.Equal(RadiclePin.Commit, match.Groups[1].Value);
    }
}
