using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OpenCommonwealth.Services;

public enum GeSaveStatus { Ok, Missing, Corrupt, UnsupportedVersion, Unreadable }

public sealed record GeSaveRead(GeSaveStatus Status, IReadOnlyDictionary<string, byte[]> Sections, string Message);

public static class GeSaveContainer
{
    public const int CurrentVersion = 1;
    public const int MaxFileBytes = 64 * 1024 * 1024;
    public const int MaxSections = 64;
    public const int MaxNameBytes = 64;
    public const string Extension = ".sav";

    private const int HeaderBytes = 8;
    private const int HashBytes = 32;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("GESV");

    public static byte[] Write(IReadOnlyDictionary<string, byte[]> sections)
    {
        if (sections.Count > MaxSections) throw new ArgumentException("too many sections");
        long size = HeaderBytes + HashBytes;
        foreach (var (name, payload) in sections)
        {
            if (!ValidName(name)) throw new ArgumentException($"bad section name '{name}'");
            size += 2 + name.Length + 4 + (long)payload.Length;
        }
        if (size > MaxFileBytes) throw new ArgumentException("the save is too large");
        using var body = new MemoryStream((int)(size - HashBytes));
        using (var writer = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write((ushort)CurrentVersion);
            writer.Write((ushort)sections.Count);
            foreach (var (name, payload) in sections.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!ValidName(name)) throw new ArgumentException($"bad section name '{name}'");
                var nameBytes = Encoding.ASCII.GetBytes(name);
                writer.Write((ushort)nameBytes.Length);
                writer.Write(nameBytes);
                writer.Write((uint)payload.Length);
                writer.Write(payload);
            }
        }
        var content = body.ToArray();
        return content.Concat(SHA256.HashData(content)).ToArray();
    }

    public static GeSaveRead Read(byte[] file)
    {
        if (file.Length > MaxFileBytes) return Fail(GeSaveStatus.Corrupt, "the save is larger than the reader accepts");
        if (file.Length < HeaderBytes + HashBytes) return Fail(GeSaveStatus.Corrupt, "the save is too short");
        if (!file.AsSpan(0, Magic.Length).SequenceEqual(Magic.AsSpan())) return Fail(GeSaveStatus.Corrupt, "not a Garden save");
        int version = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4));
        if (version > CurrentVersion)
            return Fail(GeSaveStatus.UnsupportedVersion, $"save version {version} is newer than this build reads ({CurrentVersion})");
        if (version != CurrentVersion) return Fail(GeSaveStatus.Corrupt, $"unknown save version {version}");

        int bodyEnd = file.Length - HashBytes;
        if (!SHA256.HashData(file.AsSpan(0, bodyEnd)).AsSpan().SequenceEqual(file.AsSpan(bodyEnd))) return Fail(GeSaveStatus.Corrupt, "checksum mismatch");

        int count = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(6));
        if (count > MaxSections) return Fail(GeSaveStatus.Corrupt, "too many sections");
        var sections = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var cursor = new Cursor(file, bodyEnd);
        for (int i = 0; i < count; i++)
        {
            if (!cursor.Number(2, out var nameLength) || nameLength == 0 || nameLength > MaxNameBytes || !cursor.Bytes(nameLength, out var nameBytes))
                return Fail(GeSaveStatus.Corrupt, "a section name is cut short");
            var name = Encoding.ASCII.GetString(nameBytes);
            if (!ValidName(name)) return Fail(GeSaveStatus.Corrupt, "a section name is not valid");
            if (!cursor.Number(4, out var length) || !cursor.Bytes(length, out var payload))
                return Fail(GeSaveStatus.Corrupt, $"section {name} is cut short");
            if (!sections.TryAdd(name, payload)) return Fail(GeSaveStatus.Corrupt, $"section {name} appears twice");
        }
        if (!cursor.Done) return Fail(GeSaveStatus.Corrupt, "bytes after the last section");
        return new GeSaveRead(GeSaveStatus.Ok, sections, "");
    }

    public static GeSaveRead ReadFile(string path)
    {
        if (!File.Exists(path)) return Fail(GeSaveStatus.Missing, "no save file");
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) return Fail(GeSaveStatus.Corrupt, "the save is larger than the reader accepts");
            return Read(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(GeSaveStatus.Unreadable, ex.Message);
        }
    }

    public static string? WriteFile(string path, byte[] bytes)
    {
        string temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception ex)
        {
            TryDelete(temp);
            return ex.Message;
        }
    }

    public static bool ValidName(string name) =>
        name.Length is > 0 and <= MaxNameBytes && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static GeSaveRead Fail(GeSaveStatus status, string message) =>
        new(status, new Dictionary<string, byte[]>(), message);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
        }
    }

    private sealed class Cursor(byte[] data, int end)
    {
        private int _at = HeaderBytes;

        public bool Done => _at == end;

        public bool Number(int width, out uint value)
        {
            value = 0;
            if (width > end - _at) return false;
            value = width == 2
                ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_at))
                : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_at));
            _at += width;
            return true;
        }

        public bool Bytes(uint count, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (count > (uint)(end - _at)) return false;
            bytes = data.AsSpan(_at, (int)count).ToArray();
            _at += (int)count;
            return true;
        }
    }
}
