using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Seq;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class SeqStartupConverterTests
{
    private static readonly (string Key, string Status)[] ProvenRows =
    {
        (SeqStartupConverter.ArrayKey, "PROVEN"),
        (SeqStartupConverter.EmptyFileKey, "UNPROVEN"),
        (SeqStartupConverter.PrecedenceKey, "UNPROVEN"),
        (SeqStartupConverter.MasterIndexKey, "UNPROVEN"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] rows)
    {
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static SeqInput Decoded(string plugin, params uint[] formIds) =>
        new($"{plugin}.seq", plugin, SeqInputState.Decoded, formIds);

    private static IEnumerable<SeqInput[]> Permutations(IReadOnlyList<SeqInput> inputs)
    {
        if (inputs.Count == 0)
        {
            yield return Array.Empty<SeqInput>();
            yield break;
        }
        for (var index = 0; index < inputs.Count; ++index)
        {
            var rest = inputs.Where((_, position) => position != index).ToList();
            foreach (var tail in Permutations(rest))
            {
                yield return new[] { inputs[index] }.Concat(tail).ToArray();
            }
        }
    }

    [Fact]
    public void FollowsOnlyProvenAndPolicyKeys()
    {
        var gate = Gate((SeqStartupConverter.ArrayKey, "UNPROVEN"),
            (SeqStartupConverter.EmptyFileKey, "UNPROVEN"),
            (SeqStartupConverter.PrecedenceKey, "UNPROVEN"),
            (SeqStartupConverter.MasterIndexKey, "UNPROVEN"));

        var report = SeqStartupConverter.Convert(new[] { Decoded("Mod.esp", 0x07000800) }, gate);

        Assert.Empty(report.Startup);
        Assert.Equal(0, report.Entries);
        Assert.Equal(new[] { SeqStartupConverter.ContainerNotProven }, report.ApproximationReasons);
    }

    [Fact]
    public void DecodedEntriesBecomeStartupDataWithRawFormIds()
    {
        var report = SeqStartupConverter.Convert(
            new[] { Decoded("Mod.esp", 0x07000800, 0x01001733) }, Gate(ProvenRows));

        Assert.Equal(2, report.Entries);
        Assert.Equal(1, report.Decoded);
        Assert.Equal(new[] { "Mod.esp" }, report.Startup.Select(entry => entry.Plugin).Distinct());
        Assert.Equal(0x07000800u, report.Startup[0].RawFormId);
        Assert.Equal(0x01001733u, report.Startup[1].RawFormId);
        Assert.Contains(SeqStartupConverter.MasterIndexReason, report.ApproximationReasons);
    }

    [Fact]
    public void AnEmptyFileIsReportedAsAnApproximation()
    {
        var report = SeqStartupConverter.Convert(
            new[] { Decoded("Empty.esp"), Decoded("Mod.esp", 0x07000800) }, Gate(ProvenRows));

        Assert.Equal(1, report.Empty);
        Assert.Equal(1, report.Entries);
        Assert.Contains(SeqStartupConverter.EmptyFileReason, report.ApproximationReasons);
    }

    [Fact]
    public void ANotReadFileIsReportedSeparately()
    {
        var report = SeqStartupConverter.Convert(new[]
        {
            new SeqInput("Broken.seq", "Broken.esp", SeqInputState.NotRead, Array.Empty<uint>()),
            Decoded("Mod.esp", 0x07000800),
        }, Gate(ProvenRows));

        Assert.Equal(new[] { "Broken.seq" }, report.NotReadFiles);
        Assert.Equal(1, report.NotRead);
        Assert.Equal(1, report.Decoded);
    }

    [Fact]
    public void MultiplePluginsAreNeverMerged()
    {
        var report = SeqStartupConverter.Convert(new[]
        {
            Decoded("A.esp", 0x07000800),
            Decoded("B.esp", 0x07000802),
        }, Gate(ProvenRows));

        Assert.Equal(2, report.Entries);
        Assert.Equal(new[] { "A.esp", "B.esp" }, report.Startup.Select(entry => entry.Plugin).ToArray());
        Assert.Contains(SeqStartupConverter.PrecedenceReason, report.ApproximationReasons);
    }

    [Fact]
    public void TheReportIsDeterministicUnderAnyInputOrder()
    {
        var inputs = new List<SeqInput>
        {
            Decoded("B.esp", 0x07000802),
            Decoded("A.esp", 0x07000800),
            new SeqInput("Broken.seq", "C.esp", SeqInputState.NotRead, Array.Empty<uint>()),
            Decoded("D.esp"),
        };

        var expected = SeqStartupConverter.Convert(inputs, Gate(ProvenRows)).ToReport();
        foreach (var permutation in Permutations(inputs))
        {
            Assert.Equal(expected, SeqStartupConverter.Convert(permutation, Gate(ProvenRows)).ToReport());
        }
    }

    [Fact]
    public void TheWriteIsCrashSafe()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencw_seq_startup_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "quest-startup.json");
        try
        {
            var first = SeqStartupConverter.Convert(new[] { Decoded("Mod.esp", 0x07000800) }, Gate(ProvenRows));
            SeqStartupConverter.Write(first, path);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".incoming"));
            Assert.False(File.Exists(path + ".obsolete"));

            var second = SeqStartupConverter.Convert(new[] { Decoded("Mod.esp", 0x07000800, 0x07000802) }, Gate(ProvenRows));
            SeqStartupConverter.Write(second, path);
            var text = File.ReadAllText(path);
            Assert.Contains("\"entries\": 2", text, StringComparison.Ordinal);
            Assert.False(File.Exists(path + ".obsolete"));

            File.WriteAllText(path + ".incoming", "partial");
            SeqStartupConverter.Recover(path);
            Assert.False(File.Exists(path + ".incoming"));
            Assert.Contains("\"entries\": 2", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void MasterIndexesAreNeverResolved()
    {
        var report = SeqStartupConverter.Convert(new[] { Decoded("Mod.esp", 0x01001733) }, Gate(ProvenRows));

        var entry = Assert.Single(report.Startup);
        Assert.Equal(0x01001733u, entry.RawFormId);
        Assert.Contains(SeqStartupConverter.MasterIndexReason, report.ApproximationReasons);
        Assert.DoesNotContain("FormKey", report.ToReport(), StringComparison.Ordinal);
    }
}
