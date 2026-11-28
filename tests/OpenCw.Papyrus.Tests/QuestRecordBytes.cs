using System.Text;
using PluginFile = Radicle.Interop.PluginFile;

namespace OpenCw.Papyrus.Tests;

internal static class QuestRecordBytes
{
    internal const string PluginName = "QuestProbe.esp";

    internal static byte[] StartupQuest()
    {
        var vmad = Bytes(writer =>
        {
            writer.Write((ushort)5); writer.Write((ushort)2); writer.Write((ushort)1);
            Text(writer, "QuestScript"); writer.Write((byte)0); writer.Write((ushort)4);
            void Property(string name, byte type) { Text(writer, name); writer.Write(type); writer.Write((byte)1); }
            Property("Count", 3); writer.Write(17);
            Property("SignedZero", 4); writer.Write(0x80000000u);
            Property("Flag", 5); writer.Write((byte)0);
            Property("Text", 2); Text(writer, "a text");
        });
        using var file = PluginFile.Create(PluginName);
        file.SetNextFormId(0x800);
        var quest = file.CreateRawRecord("QUST", 131);
        if (quest.LocalId != 0x800) throw new InvalidOperationException($"Radicle allocated {quest.LocalId:X} instead of 800");
        file.SetSubrecordBytes(quest, "DNAM", Bytes(writer => { writer.Write((ushort)1); writer.Write(new byte[10]); }));
        file.SetSubrecordBytes(quest, "VMAD", vmad);
        file.SetSubrecordBytes(quest, "INDX", new byte[] { 10, 0, 0, 0 }, 0);
        file.SetSubrecordBytes(quest, "INDX", new byte[] { 20, 0, 2, 0 }, 1);
        return file.ToBytes();
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        return stream.ToArray();
    }

    private static void Text(BinaryWriter writer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        writer.Write((ushort)bytes.Length); writer.Write(bytes);
    }
}
