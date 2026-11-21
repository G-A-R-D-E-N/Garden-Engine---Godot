using System;
using System.IO;
using System.Text;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Actors;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class RadicleBaseScriptsTests
{
    private const string PluginName = "NpcScripts.esp";
    private const string RefKey = "00AA:NpcScripts.esp";

    private static FormKey Key(uint id) => new(PluginName, id);

    // Detects the recruit gate reading the placed marker only: vanilla companions carry CompanionActorScript in the NPC_ base VMAD, so a marker with no scripts must still be recruitable.
    [Fact]
    public void ARecruitIsOfferedWhenOnlyTheBaseRecordCarriesTheCompanionScript()
    {
        WithNpcs(environment =>
        {
            var marker = ScriptAttachments.Parse("[]");
            bool Possible(uint id) => CompanionNpcScript.IsPossibleCompanion(marker, () => RadicleBaseScripts.Names(environment, Key(id)));

            Assert.True(CompanionRecruit.CanAsk(Possible(0x800), new CompanionRoster(), RefKey));
            Assert.False(CompanionRecruit.CanAsk(Possible(0x801), new CompanionRoster(), RefKey));
            Assert.False(Possible(0x802));
            Assert.False(Possible(0x8FE));
            Assert.Equal(new[] { "WorkshopNPCScript", "companionactorscript" }, RadicleBaseScripts.Names(environment, Key(0x800)));
        });
    }

    [Fact]
    public void AMarkerScriptDecidesWithoutReadingTheBaseRecord()
    {
        var marker = ScriptAttachments.Parse("[{\"name\":\"CompanionActorScript\",\"properties\":[]}]");

        Assert.True(CompanionNpcScript.IsPossibleCompanion(marker, () => throw new InvalidOperationException("the base record must not be read")));
    }

    private static byte[] Vmad(params string[] scripts)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((short)6);
        writer.Write((short)2);
        writer.Write((ushort)scripts.Length);
        foreach (var script in scripts)
        {
            var bytes = Encoding.UTF8.GetBytes(script);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
            writer.Write((byte)0);
            writer.Write((ushort)0);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static void WithNpcs(Action<RadicleEnvironment> check)
    {
        using var file = PluginFile.Create(PluginName);
        file.SetNextFormId(0x800);
        var companion = file.CreateRawRecord("NPC_", 131);
        file.SetSubrecordBytes(companion, "VMAD", Vmad("WorkshopNPCScript", "companionactorscript"));
        file.SetNextFormId(0x801);
        var other = file.CreateRawRecord("NPC_", 131);
        file.SetSubrecordBytes(other, "VMAD", Vmad("WorkshopNPCScript"));
        file.SetNextFormId(0x802);
        file.CreateRawRecord("NPC_", 131);

        var directory = Directory.CreateTempSubdirectory("garden-npc-scripts-");
        try
        {
            var path = Path.Combine(directory.FullName, PluginName);
            file.Write(path);
            using var environment = RadicleEnvironment.Load(new[] { path });
            check(environment);
        }
        finally { directory.Delete(true); }
    }
}
