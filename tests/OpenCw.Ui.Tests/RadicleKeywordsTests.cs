using System;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class RadicleKeywordsTests
{
    private const string PluginName = "NpcKeywords.esp";

    private static FormKey Key(uint id) => new(PluginName, id);

    [Fact]
    public void KeywordsAreTheTypedKwdaListAndUnknownWhenKsizDisagrees()
    {
        WithNpcs(environment =>
        {
            Assert.Equal(new[] { Key(0x851), Key(0x850) }, RadicleKeywords.Of(environment, Key(0x80C)));
            Assert.Empty(RadicleKeywords.Of(environment, Key(0x800))!);
            Assert.Null(RadicleKeywords.Of(environment, Key(0x80D)));
            Assert.Null(RadicleKeywords.Of(environment, Key(0x8FE)));
        });
    }

    private static void WithNpcs(Action<RadicleEnvironment> check)
    {
        using var file = PluginFile.Create(PluginName);
        file.SetNextFormId(0x800);
        file.CreateRawRecord("NPC_", 131);
        file.SetNextFormId(0x80C);
        var matching = file.CreateRawRecord("NPC_", 131);
        file.SetSubrecordBytes(matching, "KSIZ", Ids(2));
        file.SetSubrecordBytes(matching, "KWDA", Ids(0x851, 0x850));
        file.SetNextFormId(0x80D);
        var mismatched = file.CreateRawRecord("NPC_", 131);
        file.SetSubrecordBytes(mismatched, "KSIZ", Ids(3));
        file.SetSubrecordBytes(mismatched, "KWDA", Ids(0x850));

        var directory = Directory.CreateTempSubdirectory("garden-npc-keywords-");
        try
        {
            var path = Path.Combine(directory.FullName, PluginName);
            file.Write(path);
            using var environment = RadicleEnvironment.Load(new[] { path });
            check(environment);
        }
        finally { directory.Delete(true); }
    }

    private static byte[] Ids(params uint[] ids) => ids.SelectMany(id => BitConverter.GetBytes(id)).ToArray();
}
