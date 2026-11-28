using System.IO;
using System.Linq;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlayerMovementDelegationTests
{
    static string Source(params string[] path)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(PlayerMovementDelegationTests).Assembly.Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "opencw", "scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "opencw" }.Concat(path).ToArray()));
    }

    [Fact]
    public void The_movement_slice_wires_the_exported_session_and_read_lock_only()
    {
        var source = Source("scripts", "Player", "PlayerController.Movement.cs");
        Assert.Contains("MovementReadSession<MovementSet>", source, StringComparison.Ordinal);
        Assert.Contains("MovementLoad.WithReadLock", source, StringComparison.Ordinal);
        Assert.Contains("VanillaMovement.Read", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MovementReadGate.Resolve", source, StringComparison.Ordinal);
    }
}