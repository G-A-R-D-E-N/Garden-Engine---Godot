using System;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class AtomicTextFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "OcAtomic_" + Guid.NewGuid().ToString("N"));

    public AtomicTextFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    private string[] Files() => Directory.GetFileSystemEntries(_directory).Select(n => Path.GetFileName(n)!).OrderBy(n => n).ToArray();

    [Fact]
    public void An_overwrite_replaces_the_content_and_leaves_no_temp_file()
    {
        var path = Path.Combine(_directory, "Look 1.json");
        Assert.True(AtomicTextFile.Write(path, "first", overwrite: true));
        Assert.True(AtomicTextFile.Write(path, "second", overwrite: true));

        Assert.Equal("second", File.ReadAllText(path));
        Assert.Equal(new[] { "Look 1.json" }, Files());
    }

    [Fact]
    public void An_exclusive_create_refuses_an_existing_file_and_keeps_it()
    {
        var path = Path.Combine(_directory, "Look 1.json");
        Assert.True(AtomicTextFile.Write(path, "first", overwrite: false));
        Assert.False(AtomicTextFile.Write(path, "second", overwrite: false));

        Assert.Equal("first", File.ReadAllText(path));
        Assert.Equal(new[] { "Look 1.json" }, Files());
    }

    [Fact]
    public void A_failed_replace_throws_and_leaves_neither_a_temp_file_nor_a_changed_target()
    {
        var path = Path.Combine(_directory, "Look 1.json");
        Directory.CreateDirectory(path);

        Assert.ThrowsAny<Exception>(() => { AtomicTextFile.Write(path, "second", overwrite: true); });

        Assert.True(Directory.Exists(path));
        Assert.Equal(new[] { "Look 1.json" }, Files());
    }

    [Fact]
    public void The_text_is_written_as_utf8_without_a_byte_order_mark()
    {
        var path = Path.Combine(_directory, "x.json");
        Assert.True(AtomicTextFile.Write(path, "café", overwrite: true));

        Assert.Equal(new byte[] { 0x63, 0x61, 0x66, 0xC3, 0xA9 }, File.ReadAllBytes(path));
    }
}
