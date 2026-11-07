using System.Text;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestStartupCacheTests
{
    private static ScriptCacheBuildResult Build(string root, IReadOnlyList<QuestStartupInput>? inputs = null)
    {
        var payload = Encoding.UTF8.GetBytes(PapyrusIr.ToJson(new[]
        {
            new PapyrusScript("QuestScript", Array.Empty<PapyrusFunction>(), Array.Empty<string>()),
        }));
        return ScriptCacheBuilder.Build(new[]
        {
            new ScriptSourceCandidate("QuestScript", ScriptSourceKind.Loose, "project-authored", 0,
                payload.Length, () => payload),
        }, "project-authored", new ScriptCacheConverter(1, 1, bytes => Encoding.UTF8.GetString(bytes)), root,
            questStartup: inputs);
    }

    private static QuestStartupInput Input()
    {
        var value = new QuestStartupValue(17, null, null, null, null, null, null, null);
        var property = new QuestStartupProperty("QuestScript", "Count", 3, 1, value);
        var zero = new QuestStartupProperty("QuestScript", "SignedZero", 4, 1,
            new QuestStartupValue(null, unchecked((int)0x80000000), null, null, null, null, null, null));
        var text = new QuestStartupProperty("QuestScript", "Text", 2, 1,
            new QuestStartupValue(null, null, null, "a\0text", null, null, null, null));
        var link = new QuestStartupProperty("QuestScript", "Link", 1, 1,
            new QuestStartupValue(null, null, null, null, 0x123, -1, null, "000123:QuestProbe.esp"));
        var alias = new QuestStartupProperty("AliasScript", "AliasLink", 1, 1,
            link.Value with { Alias = 3, ObjectKey = null }, 3);
        return new QuestStartupInput("000800:QuestProbe.esp", true, new[] { "QuestScript" }, 1, 1,
            "QuestProbe.esp", new[] { "synthetic decode refusal" },
            new QuestStartupMetadata(new[] { property, zero, text, link }, "FragmentScript",
                new[] { property with { Script = "FragmentScript" } },
                new[] { new QuestStartupFragment(20, 2, "FragmentScript", "Fragment_2") }, new[] { 3 },
                new[] { new QuestStartupAliasScript(3, "AliasScript") }, new[] { alias }));
    }

    [Fact]
    public void NonemptyStartupMetadataRoundTripsWithTheScriptSnapshot()
    {
        using var cache = new TempCache();
        var input = Input();
        var built = Build(cache.Root, new[] { input });
        var loaded = Assert.Single(GameSession.Load(cache.Root).QuestStartup);
        Assert.Equal(new QuestStartupDatabase(1, new[] { input }).ToJson(),
            new QuestStartupDatabase(1, new[] { loaded }).ToJson());
        Assert.Equal(1, built.Manifest.QuestStartupCount);
        Assert.Equal(ScriptCacheBuilder.Fingerprint(File.ReadAllBytes(ScriptCacheLayout.QuestStartupPath(cache.Root))),
            built.Manifest.QuestStartupSha256);
        Assert.Equal(unchecked((int)0x80000000), loaded.Metadata!.ScriptProperties[1].Value.RealBits);
        Assert.Equal(3, loaded.Metadata.AliasProperties[0].Alias);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void IncompleteMetadataCannotBecomeAValidStartupSnapshot(int defect)
    {
        var input = Input();
        input = defect switch
        {
            0 => input with { Metadata = null },
            1 => input with { Fragments = 2 },
            2 => input with { Aliases = 2 },
            3 => input with { Metadata = input.Metadata! with { ScriptProperties = null! } },
            _ => input with { Metadata = input.Metadata! with
                { ScriptProperties = new[] { input.Metadata.ScriptProperties[0] with { Value = null! } } } },
        };
        Assert.Throws<InvalidOperationException>(() => QuestStartupDatabase.FromJson(
            new QuestStartupDatabase(1, new[] { input }).ToJson()));
    }

    [Fact]
    public void QuestOnlyChangesRebuildTheSnapshotWithoutReusingStaleStartupData()
    {
        using var cache = new TempCache();
        var first = Build(cache.Root, new[] { Input() });
        var second = Build(cache.Root, new[] { Input() with { Quest = "000900:QuestProbe.esp" } });
        Assert.Equal(1, second.ReusedCount);
        Assert.Equal(0, second.ConvertedCount);
        Assert.NotEqual(first.Manifest.QuestStartupSha256, second.Manifest.QuestStartupSha256);
        Assert.Equal("000900:QuestProbe.esp", Assert.Single(GameSession.Load(cache.Root).QuestStartup).Quest);
    }

    [Fact]
    public void RecoveryRestoresQuestDataAndScriptIrTogether()
    {
        using var cache = new TempCache();
        var first = Build(cache.Root, new[] { Input() });
        File.WriteAllText(ScriptCacheLayout.MarkerPath(cache.Root), "pending");
        Directory.Move(ScriptCacheLayout.CurrentDirectory(cache.Root), ScriptCacheLayout.ObsoleteDirectory(cache.Root));
        var incoming = ScriptCacheLayout.IncomingDirectory(cache.Root);
        Directory.CreateDirectory(incoming);
        File.WriteAllText(Path.Combine(incoming, ScriptCacheLayout.QuestStartupFileName), "partial");
        ScriptCacheBuilder.Recover(cache.Root);
        Assert.Equal(first.Manifest.QuestStartupSha256, ScriptCacheBuilder.Load(cache.Root)!.QuestStartupSha256);
        var recovered = GameSession.Load(cache.Root);
        Assert.Equal(Input().Quest, Assert.Single(recovered.QuestStartup).Quest);
        Assert.True(recovered.Host.HasScript("QuestScript"));
        Assert.False(Directory.Exists(incoming));
    }

    [Fact]
    public void EveryCacheHasAnExplicitQuestStartupPayloadEvenWhenEmpty()
    {
        using var cache = new TempCache();
        Build(cache.Root);
        Assert.True(File.Exists(Path.Combine(ScriptCacheLayout.CurrentDirectory(cache.Root), "quests.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadingRefusesAnUnversionedOrChangedQuestStartupPayload(bool missing)
    {
        using var cache = new TempCache();
        Build(cache.Root);
        var path = Path.Combine(ScriptCacheLayout.CurrentDirectory(cache.Root), "quests.json");
        if (missing) File.Delete(path);
        else File.WriteAllText(path, "{\"FormatVersion\":999,\"Inputs\":[]}");
        Assert.ThrowsAny<Exception>(() => GameSession.Load(cache.Root));
    }
}
