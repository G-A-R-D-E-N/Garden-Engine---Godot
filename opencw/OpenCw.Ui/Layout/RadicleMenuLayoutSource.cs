using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Layout;

public sealed class RadicleMenuLayoutSource : IMenuLayoutSource
{
    public delegate SwfLayoutRecord LayoutReader(MenuSwfRef source);

    private readonly IReadOnlyList<MenuSwfRef> _refs;
    private readonly LayoutReader _read;
    private readonly List<string> _failures = [];

    public RadicleMenuLayoutSource(IReadOnlyList<MenuSwfRef> refs, LayoutReader read, string? installFingerprint)
    {
        ArgumentNullException.ThrowIfNull(refs);
        ArgumentNullException.ThrowIfNull(read);
        _refs = refs;
        _read = read;
        InstallFingerprint = installFingerprint;
    }

    public int AbiVersion => (int)RadicleLibrary.AbiVersion;

    public string? InstallFingerprint { get; }

    public IReadOnlyList<string> Failures => _failures;

    public IReadOnlyList<MenuLayout> ReadAll()
    {
        _failures.Clear();
        var layouts = new List<MenuLayout>();
        foreach (var source in _refs)
        {
            try
            {
                var record = _read(source);
                if (!(record.Stage.Width > 0 && record.Stage.Height > 0))
                {
                    _failures.Add($"{source.Menu}: the movie has an empty stage");
                    continue;
                }
                var converted = SwfLayoutConversion.ToMenuLayouts(source, record);
                if (converted.Count == 0)
                {
                    _failures.Add($"{source.Menu}: no drawable rows");
                    continue;
                }
                layouts.AddRange(converted);
            }
            catch (Exception ex)
            {
                _failures.Add($"{source.Menu}: {ex.Message}");
            }
        }
        return layouts;
    }

    public static RadicleMenuLayoutSource Open(string mappingPath)
    {
        var json = File.ReadAllText(mappingPath);
        var refs = Resolve(MenuSwfMapping.Parse(json), Path.GetDirectoryName(Path.GetFullPath(mappingPath))!);
        return new RadicleMenuLayoutSource(refs, ReadFromRadicle, Fingerprint(json, refs));
    }

    public static IReadOnlyList<MenuSwfRef> Resolve(IReadOnlyList<MenuSwfRef> refs, string baseDirectory) =>
        refs.Select(source => source.Archive is { } archive
            ? source with { Archive = FullPath(archive, baseDirectory) }
            : source with { Entry = FullPath(source.Entry, baseDirectory) }).ToList();

    private static string FullPath(string path, string baseDirectory)
    {
        try
        {
            return Path.GetFullPath(path, baseDirectory);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    public static string Fingerprint(string mappingJson, IReadOnlyList<MenuSwfRef> refs)
    {
        var builder = new StringBuilder(mappingJson);
        foreach (var path in refs.Select(source => source.Archive ?? source.Entry).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            builder.Append('\n').Append(path).Append('|');
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                builder.Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static SwfLayoutRecord ReadFromRadicle(MenuSwfRef source)
    {
        if (source.Archive is null)
        {
            using var loose = SwfFile.FromFile(source.Entry);
            return loose.Layout();
        }
        using var archive = Ba2Archive.Open(source.Archive);
        using var swf = SwfFile.FromArchive(archive, source.Entry);
        return swf.Layout();
    }
}
