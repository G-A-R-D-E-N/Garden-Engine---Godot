using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ArchiveEntrySelectionTests
{
    private static readonly (string EntryPath, long ByteSize)[] Entries =
    {
        ("Meshes/Thing.nif", 10),
        ("Scripts/Quest/QF_Test.pex", 100),
        ("SCRIPTS/Other.PEX", 200),
        ("Scripts/Notes.psc", 300),
        ("Scripts/", 400),
    };

    private static IEnumerable<ScriptSourceCandidate> Enumerate(Func<int, byte[]>? read = null) =>
        ArchiveScriptSource.Enumerate("Fallout4 - Misc.ba2", 3, Entries, read ?? (_ => Array.Empty<byte>()));

    [Fact]
    public void OnlyScriptEntriesBecomeCandidates()
    {
        var names = Enumerate().Select(candidate => candidate.Name).ToArray();

        Assert.Equal(new[] { "quest/qf_test", "other" }, names);
    }

    [Fact]
    public void ThePrefixAndTheExtensionMatchCaseInsensitively()
    {
        var names = Enumerate().Select(candidate => candidate.Name).ToArray();

        Assert.Contains("other", names);
        Assert.DoesNotContain("notes", names);
    }

    [Fact]
    public void ACandidateCarriesItsArchiveItsLoadIndexAndItsSize()
    {
        var candidate = Enumerate().First();

        Assert.Equal(ScriptSourceKind.Archive, candidate.Kind);
        Assert.Equal("Fallout4 - Misc.ba2", candidate.ContainerPath);
        Assert.Equal(3, candidate.LoadIndex);
        Assert.Equal(100, candidate.ByteSize);
    }

    [Fact]
    public void TheReaderRunsOnlyWhenThePayloadIsWantedAndGetsTheEntryPosition()
    {
        var asked = new List<int>();
        var candidates = Enumerate(position =>
        {
            asked.Add(position);
            return new byte[] { (byte)position };
        }).ToArray();

        Assert.Empty(asked);
        Assert.Equal(new byte[] { 1 }, candidates[0].Read());
        Assert.Equal(new byte[] { 2 }, candidates[1].Read());
        Assert.Equal(new[] { 1, 2 }, asked);
    }

    [Fact]
    public void AnArchiveWithNoScriptsYieldsNothing()
    {
        var candidates = ArchiveScriptSource.Enumerate("empty.ba2", 0,
            new[] { ("Meshes/Thing.nif", 1L) }, _ => Array.Empty<byte>());

        Assert.Empty(candidates);
    }

    [Fact]
    public void AnArchiveNameMatchesWhatALooseScriptOfTheSamePathNormalizesTo()
    {
        var fromArchive = ArchiveScriptSource.ScriptName("Scripts/Quest/QF_Test.PEX");
        var fromLoose = ScriptCachePrecedence.NormalizeName(@"Quest\QF_Test.PEX");

        Assert.Equal("quest/qf_test", fromLoose);
        Assert.Equal(fromLoose, fromArchive);
    }

    [Fact]
    public void PathsThatAreNotScriptsHaveNoName()
    {
        Assert.Null(ArchiveScriptSource.ScriptName("Meshes/Thing.nif"));
        Assert.Null(ArchiveScriptSource.ScriptName("Scripts/Notes.psc"));
        Assert.Null(ArchiveScriptSource.ScriptName("Scripts/"));
        Assert.Null(ArchiveScriptSource.ScriptName(""));
    }

    [Theory]
    [InlineData("../../outside")]
    [InlineData("C:/outside")]
    [InlineData("/outside")]
    [InlineData("quest/../outside")]
    [InlineData("quest\\..\\outside")]
    public void UnsafeNamesCannotEscapeTheCache(string name)
    {
        Assert.Null(ArchiveScriptSource.ScriptName("Scripts/" + name + ".pex"));
        Assert.Throws<ArgumentException>(() => ScriptCacheLayout.ScriptPathIn("incoming", name));
    }

    [Fact]
    public void TheArchiveSourceReportsItselfAvailableAndNamesItsReader()
    {
        Assert.True(ArchiveScriptSource.Availability.Available);
        Assert.Contains("Radicle", ArchiveScriptSource.Availability.Reason);
    }

    [Fact]
    public void AMissingEntryListOrReaderIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => ArchiveScriptSource
            .Enumerate("a.ba2", 0, null!, _ => Array.Empty<byte>()).ToArray());
        Assert.Throws<ArgumentNullException>(() => ArchiveScriptSource
            .Enumerate("a.ba2", 0, Entries, null!).ToArray());
    }
}
