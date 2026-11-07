using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Cache;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ArchiveScriptSourceTests
{
    private sealed class FakeArchive : IArchiveReader
    {
        private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

        public void Add(string archive, string entry, int size) =>
            _entries[$"{archive}!{entry}"] = Encoding.UTF8.GetBytes(new string('x', size));

        public bool TryRead(string archivePath, string entryPath, out byte[] bytes) =>
            _entries.TryGetValue($"{archivePath}!{entryPath}", out bytes!);
    }

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EvidenceKeys.Frames] = "ENGINE_POLICY",
            [EvidenceKeys.OnInitBarrier] = "ENGINE_POLICY",
            [EvidenceKeys.EventDuringOnInit] = "APPROXIMATED",
            [EvidenceKeys.SchedulerBudget] = "ENGINE_POLICY",
            [EvidenceKeys.Host] = "ENGINE_POLICY",
            [EvidenceKeys.FailClosed] = "ENGINE_POLICY",
            [EvidenceKeys.NativeTarget] = "ENGINE_POLICY",
            [ScriptOverrideRegistry.Key] = "ENGINE_POLICY",
            [ScriptOverrideRegistry.ReflectionKey] = "ENGINE_POLICY",
            [ArchiveScriptSource.Key] = "PROVEN",
        };
        foreach (var pair in overrides)
        {
            rows[pair.Key] = pair.Status;
        }
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static readonly (string Archive, string Entry)[] Requests =
    {
        ("Fallout4 - Misc.ba2", "scripts\\QF_Test.pex"),
        ("DLCworkshop01 - Main.ba2", "scripts\\QF_Dlc.pex"),
    };

    [Fact]
    public void ScriptsAreReadThroughTheReaderAndCounted()
    {
        var reader = new FakeArchive();
        reader.Add("Fallout4 - Misc.ba2", "scripts\\QF_Test.pex", 128);
        reader.Add("DLCworkshop01 - Main.ba2", "scripts\\QF_Dlc.pex", 64);

        var report = new ArchiveScriptSource(reader, Gate()).Extract(Requests, Array.Empty<string>());

        Assert.Equal(2, report.Scripts.Count);
        Assert.Empty(report.Refused);
        Assert.Contains(new ArchiveScript("Fallout4 - Misc.ba2", "scripts\\QF_Test.pex", "QF_Test", 128), report.Scripts);
        Assert.Contains(new ArchiveScript("DLCworkshop01 - Main.ba2", "scripts\\QF_Dlc.pex", "QF_Dlc", 64), report.Scripts);
    }

    [Fact]
    public void AMissingEntryIsRefusedAndNeverReportedAsEmpty()
    {
        var reader = new FakeArchive();
        reader.Add("Fallout4 - Misc.ba2", "scripts\\QF_Test.pex", 128);

        var report = new ArchiveScriptSource(reader, Gate()).Extract(Requests, Array.Empty<string>());

        var refusal = Assert.Single(report.Refused);
        Assert.Equal("DLCworkshop01 - Main.ba2", refusal.ArchivePath);
        Assert.Contains("not treated as an empty script", refusal.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(report.Scripts, script => script.ScriptName == "QF_Dlc");
    }

    [Fact]
    public void ALooseScriptShadowsTheArchivedCopy()
    {
        var reader = new FakeArchive();
        reader.Add("Fallout4 - Misc.ba2", "scripts\\QF_Test.pex", 128);

        var report = new ArchiveScriptSource(reader, Gate())
            .Extract(new[] { ("Fallout4 - Misc.ba2", "scripts\\QF_Test.pex") }, new[] { "QF_Test" });

        Assert.Empty(report.Scripts);
        var refusal = Assert.Single(report.Refused);
        Assert.Contains("shadows the archived copy", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnprovenKeyReadsNothing()
    {
        var reader = new FakeArchive();
        reader.Add("Fallout4 - Misc.ba2", "scripts\\QF_Test.pex", 128);

        var source = new ArchiveScriptSource(reader, Gate((ArchiveScriptSource.Key, "UNPROVEN")));
        var report = source.Extract(Requests, Array.Empty<string>());

        Assert.Empty(report.Scripts);
        Assert.Equal(2, report.Refused.Count);
        Assert.Equal(2, source.Refused);
        Assert.All(report.Refused, refusal => Assert.Contains("not proven", refusal.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public void ALooseScriptShadowsAnArchivedNameWithDifferentCase()
    {
        var reader = new FakeArchive();
        reader.Add("test.ba2", "Scripts/QF_Test.PEX", 128);
        var source = new ArchiveScriptSource(reader, Gate());

        var report = source.Extract(new[] { ("test.ba2", "Scripts/QF_Test.PEX") }, new[] { "qf_test" });

        Assert.Empty(report.Scripts);
        Assert.Contains("shadows the archived copy", Assert.Single(report.Refused).Reason, StringComparison.Ordinal);
        Assert.Equal(0, source.Read);
        Assert.Equal(1, source.Refused);
    }

    [Fact]
    public void TheReportIsOrdinallyOrderedAndDeterministic()
    {
        var reader = new FakeArchive();
        reader.Add("Fallout4 - Misc.ba2", "scripts\\QF_Test.pex", 128);
        reader.Add("DLCworkshop01 - Main.ba2", "scripts\\QF_Dlc.pex", 64);
        var source = new ArchiveScriptSource(reader, Gate());

        var first = source.Extract(Requests.Reverse(), Array.Empty<string>());
        var second = source.Extract(Requests, Array.Empty<string>());

        Assert.Equal("DLCworkshop01 - Main.ba2", first.Scripts[0].ArchivePath);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(64, first.Digest().Length);
    }
}
