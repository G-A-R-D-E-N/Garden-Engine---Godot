using OpenCommonwealth.Services;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ProcessTempWriteThenMoveTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("write-then-move-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Dest => Path.Combine(_dir.FullName, "clip.res");

    private string[] Leftovers() => Directory.GetFiles(_dir.FullName).Where(f => f != Dest).ToArray();

    [Fact]
    public void AFailedWriteLeavesNoDestinationSoTheItemIsRedone()
    {
        Assert.Throws<IOException>(() => ProcessTemp.WriteThenMove(Dest, temp =>
        {
            File.WriteAllBytes(temp, new byte[3]);
            throw new IOException("save failed half way");
        }));
        Assert.False(File.Exists(Dest));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public void AFailedWriteKeepsTheOldDestination()
    {
        File.WriteAllBytes(Dest, new byte[] { 1, 2, 3, 4 });
        Assert.Throws<IOException>(() => ProcessTemp.WriteThenMove(Dest, temp =>
        {
            File.WriteAllBytes(temp, new byte[] { 9 });
            throw new IOException("save failed half way");
        }));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Dest));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public void WhileWritingTheDestinationIsAbsentAndTheTempKeepsTheExtension()
    {
        ProcessTemp.WriteThenMove(Dest, temp =>
        {
            Assert.False(File.Exists(Dest));
            Assert.Equal(_dir.FullName, Path.GetDirectoryName(temp));
            Assert.EndsWith(".tmp.res", temp, StringComparison.Ordinal);
            Assert.StartsWith(".", Path.GetFileName(temp), StringComparison.Ordinal);
            File.WriteAllBytes(temp, new byte[] { 5, 6 });
        });
        Assert.Equal(new byte[] { 5, 6 }, File.ReadAllBytes(Dest));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public void AnOldDeadTempForTheSameItemIsSweptOnTheNextWrite()
    {
        var dead = Path.Combine(_dir.FullName, ".clip.tmp.res");
        File.WriteAllBytes(dead, new byte[] { 7 });
        File.SetLastWriteTimeUtc(dead, DateTime.UtcNow.AddMinutes(-20));
        ProcessTemp.WriteThenMove(Dest, temp => File.WriteAllBytes(temp, new byte[] { 1 }));
        Assert.False(File.Exists(dead));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public void AnOldDeadPidTempForTheSameItemIsSweptOnTheNextWrite()
    {
        var dead = Path.Combine(_dir.FullName, $".clip.{int.MaxValue}.tmp.res");
        File.WriteAllBytes(dead, new byte[] { 7 });
        File.SetLastWriteTimeUtc(dead, DateTime.UtcNow.AddMinutes(-20));
        ProcessTemp.WriteThenMove(Dest, temp => File.WriteAllBytes(temp, new byte[] { 1 }));
        Assert.False(File.Exists(dead));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public void AFreshDeadPidTempForTheSameItemIsSweptOnTheNextWrite()
    {
        var dead = Path.Combine(_dir.FullName, $".clip.{int.MaxValue}.tmp.res");
        File.WriteAllBytes(dead, new byte[] { 7 });
        ProcessTemp.WriteThenMove(Dest, temp => File.WriteAllBytes(temp, new byte[] { 1 }));
        Assert.False(File.Exists(dead));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public void AWriterThatProducesNothingFailsTheItem()
    {
        Assert.ThrowsAny<IOException>(() => ProcessTemp.WriteThenMove(Dest, _ => { }));
        Assert.False(File.Exists(Dest));
    }
}
