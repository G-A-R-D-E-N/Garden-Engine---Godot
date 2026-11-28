using System.Collections;
using System.Reflection;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Swf;

public sealed record SwfFontMetrics(double UnitsPerEm, IReadOnlyDictionary<char, double> Advances);

public sealed record SwfFontLocation(int TagIndex, double UnitsPerEm, double Ascent, IReadOnlyList<ushort> Codes);

public interface ISwfFontSource
{
    byte[]? Ttf(string fontClass);

    SwfFontMetrics? Metrics(string fontClass);
}

public static class SwfFontResolver
{
    public const string Regular = "$MAIN_Font";
    public const string Bold = "$MAIN_Font_Bold";

    public static string Default(bool heavy) => heavy ? Bold : Regular;

    public static byte[]? Ttf(ISwfFontSource? source, string fontClass, bool heavy)
    {
        if (source is null)
            return null;
        try
        {
            if (fontClass.Length > 0 && fontClass != Default(heavy) && source.Ttf(fontClass) is { } own)
                return own;
            return source.Ttf(Default(heavy));
        }
        catch (Exception)
        {
            return null;
        }
    }
}

// Constraint: SwfFile.FontTtf(int) and SwfFile.FontMetrics(int) are bound by name so an older Radicle pin still loads; names follow the vault note Project/Radicle font export request.md.
public sealed class ReflectedFontSource : ISwfFontSource
{
    public const string TtfMethod = "FontTtf";
    public const string MetricsMethod = "FontMetrics";
    public const string AdvancesProperty = "Advances";

    private readonly object _file;
    private readonly Func<string, SwfFontLocation?> _locate;
    private readonly MethodInfo? _ttf;
    private readonly MethodInfo? _metrics;
    private readonly Dictionary<string, byte[]?> _ttfCache = new();
    private readonly Dictionary<string, SwfFontMetrics?> _metricsCache = new();
    private bool _dead;

    public ReflectedFontSource(object file, Func<string, SwfFontLocation?> locate)
    {
        _file = file;
        _locate = locate;
        _ttf = file.GetType().GetMethod(TtfMethod, [typeof(int)]);
        _metrics = file.GetType().GetMethod(MetricsMethod, [typeof(int)]);
    }

    public static ISwfFontSource For(SwfFile file)
    {
        var located = new Dictionary<string, SwfFontLocation?>();
        return new ReflectedFontSource(file, cls =>
        {
            if (!located.TryGetValue(cls, out var hit))
                located[cls] = hit = SwfFontOutlines.Locate(file, cls);
            return hit;
        });
    }

    public static bool BindingHasTtf => typeof(SwfFile).GetMethod(TtfMethod, [typeof(int)]) is not null;

    public bool HasTtf => !_dead && _ttf is not null;

    public bool HasMetrics => !_dead && _metrics is not null;

    public byte[]? Ttf(string fontClass)
    {
        if (!HasTtf)
            return null;
        if (_ttfCache.TryGetValue(fontClass, out var hit))
            return hit;
        var bytes = Call(_ttf!, fontClass) as byte[];
        return _ttfCache[fontClass] = bytes is { Length: >= 12 } ? bytes : null;
    }

    public SwfFontMetrics? Metrics(string fontClass)
    {
        if (!HasMetrics)
            return null;
        if (_metricsCache.TryGetValue(fontClass, out var hit))
            return hit;
        return _metricsCache[fontClass] = ReadMetrics(fontClass);
    }

    private SwfFontMetrics? ReadMetrics(string fontClass)
    {
        try
        {
            if (_locate(fontClass) is not { } location || Call(_metrics!, fontClass) is not { } record)
                return null;
            if (record.GetType().GetProperty(AdvancesProperty)?.GetValue(record) is not IEnumerable advances)
                return null;
            var advance = new Dictionary<char, double>();
            var index = 0;
            foreach (var value in advances)
            {
                if (index >= location.Codes.Count)
                    break;
                advance.TryAdd((char)location.Codes[index++], Convert.ToDouble(value));
            }
            return new SwfFontMetrics(location.UnitsPerEm, advance);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private object? Call(MethodInfo method, string fontClass)
    {
        try
        {
            return _locate(fontClass) is { } location ? method.Invoke(_file, [location.TagIndex]) : null;
        }
        catch (TargetInvocationException e) when (e.InnerException is EntryPointNotFoundException)
        {
            _dead = true;
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
