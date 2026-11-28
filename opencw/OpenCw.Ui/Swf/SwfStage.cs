using System.Text.RegularExpressions;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Swf;

public readonly record struct SwfMatrix(double A, double B, double C, double D, double Tx, double Ty);

public sealed class SwfNode
{
    public SwfNode(string path, string name, int depth, ushort characterId, bool isSprite, ushort? clipDepth, SwfMatrix local, ushort? ratio = null)
    {
        Path = path;
        Name = name;
        Depth = depth;
        CharacterId = characterId;
        IsSprite = isSprite;
        ClipDepth = clipDepth;
        Local = local;
        Ratio = ratio;
    }

    public string Path { get; }
    public string Name { get; }
    public int Depth { get; }
    public ushort CharacterId { get; }
    public bool IsSprite { get; }
    public ushort? ClipDepth { get; }
    public bool IsMask => ClipDepth.HasValue;
    public SwfMatrix Local { get; }
    public ushort? Ratio { get; }
    public bool Visible { get; set; } = true;
    public (double X, double Y, double W, double H)? Bounds { get; set; }
    public SwfLayoutTextRecord? Text { get; set; }
    public SwfEffects Effects { get; set; } = SwfEffects.None;
    public List<SwfNode> Children { get; } = new();

    public static List<SwfNode> MaskedBy(SwfNode mask, IEnumerable<SwfNode> siblings) =>
        mask.ClipDepth is { } end ? siblings.Where(s => s.Depth > mask.Depth && s.Depth <= end).ToList() : new List<SwfNode>();
}

public sealed record SwfTemplate(string Name, string? Symbol, string? Path, int Frame, ushort CharacterId = 0);

public sealed record SwfReadOptions(bool Settle, IReadOnlyList<string> Open)
{
    public static readonly SwfReadOptions None = new(false, Array.Empty<string>());

    public static SwfReadOptions For(string entry) =>
        entry == ContainerArt.Movie ? ContainerArt.Read : entry == BarterArt.Movie ? BarterArt.Read : None;
}

public sealed class SwfStage : IDisposable
{
    public const string TemplatePrefix = "tpl/";

    private readonly SwfFile _file;
    private readonly Dictionary<(ushort Id, ushort Ratio), string?> _svg = new();
    private readonly Dictionary<string, ushort> _characters;

    private SwfStage(SwfFile file, SwfNode root, double width, double height, double frameRate, Dictionary<string, ushort> characters)
    {
        _file = file;
        _characters = characters;
        Root = root;
        Width = width;
        Height = height;
        FrameRate = frameRate;
    }

    public SwfNode Root { get; }
    public IReadOnlyList<string> Warnings { get; private init; } = Array.Empty<string>();
    public double Width { get; }
    public double Height { get; }
    public double FrameRate { get; }

    // Constraint: a morph shape draws at its PlaceObject ratio (0 when the placement carried none, as the frame SVG does), so the cache key is the pair.
    public string? ShapeSvg(ushort characterId, ushort? ratio = null)
    {
        var key = (characterId, ratio ?? (ushort)0);
        if (_svg.TryGetValue(key, out var cached))
        {
            if (cached is null && ratio is not null)
                MorphExportFailed = true;
            return cached;
        }
        var svg = Try(() => _file.ShapeSvg(characterId)) ?? Try(() => _file.MorphShapeSvg(characterId, key.Item2));
        if (svg is null && ratio is not null)
            MorphExportFailed = true;
        _svg[key] = svg;
        return svg;
    }

    public bool MorphExportFailed { get; private set; }

    private static string? Try(Func<string> read)
    {
        try { return read(); }
        catch (Exception) { return null; }
    }

    public void Dispose() => _file.Dispose();

    public static SwfStage Read(byte[] bytes, SwfReadOptions? options = null)
    {
        var file = SwfFile.FromBytes(bytes);
        try
        {
            var layout = file.Layout();
            var rows = new Dictionary<string, SwfLayoutRowRecord>();
            if (layout.States.Count > 0)
                foreach (var r in layout.States[0].Rows)
                    rows[r.Path] = r;

            var root = new SwfNode("", "", 0, 0, true, null, new SwfMatrix(1, 0, 0, 1, 0, 0));
            var latest = new Dictionary<string, SwfNode>();
            var characters = new Dictionary<string, ushort>();
            var warnings = new List<string>();
            foreach (var n in ReadInstances(file, options ?? SwfReadOptions.None, warnings))
            {
                characters[n.Path] = n.CharacterId;
                if (rows.TryGetValue(n.Path, out var row))
                {
                    n.Visible = row.Visible;
                    n.Text = row.Text;
                    if (row.Text is not null && row.HasRect && LocalBox(row) is { } box)
                        n.Bounds = box;
                }
                var slash = n.Path.LastIndexOf('/');
                var parent = slash > 0 && latest.TryGetValue(n.Path[..slash], out var p) ? p : root;
                parent.Children.Add(n);
                latest[n.Path] = n;
            }
            Sort(root);
            return new SwfStage(file, root, layout.Stage.Width, layout.Stage.Height, layout.FrameRate, characters) { Warnings = warnings };
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public SwfNode? ReadTemplate(SwfTemplate template)
    {
        try
        {
            ushort? character = template.CharacterId != 0
                ? template.CharacterId
                : template.Path is { } path
                    ? _characters.TryGetValue(path, out var found) ? found : null
                    : _file.Symbols.FirstOrDefault(s => s.Name == template.Symbol)?.TagId;
            if (character is null)
                return null;
            using var frames = _file.OpenFrames();
            for (var timeline = 0; timeline < frames.TimelineCount; timeline++)
            {
                var (id, frameCount) = frames.TimelineAt(timeline);
                if (id != character)
                    continue;
                if (template.Frame < 0 || template.Frame >= frameCount)
                    return null;
                return ReadTemplateTimeline(frames, timeline, template);
            }
            return null;
        }
        catch (RadicleException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static SwfNode ReadTemplateTimeline(SwfFrameSet frames, int timeline, SwfTemplate template)
    {
        var prefix = TemplatePrefix + template.Name;
        var root = new SwfNode(prefix, "tpl_" + template.Name, 0, 0, true, null, new SwfMatrix(1, 0, 0, 1, 0, 0)) { Visible = false };
        var latest = new Dictionary<string, SwfNode>();
        var instances = frames.Instances(timeline, template.Frame);
        var effectsAvailable = true;
        for (var i = 0; i < instances.Count; i++)
        {
            var ins = instances[i];
            var t = ins.Transform;
            var relative = ins.Path ?? "";
            var node = new SwfNode(prefix + "/" + relative, ins.Name ?? "", ins.Depth, ins.CharacterId, ins.IsSprite, ins.HasClipDepth ? ins.ClipDepth : (ushort?)null,
                new SwfMatrix(t.ScaleX, t.RotateSkew0, t.RotateSkew1, t.ScaleY, t.TranslateX, t.TranslateY), ins.Ratio);
            if (ins.HasBounds)
                node.Bounds = (ins.Bounds.X, ins.Bounds.Y, ins.Bounds.Width, ins.Bounds.Height);
            if (effectsAvailable)
                node.Effects = ReadEffects(frames, timeline, template.Frame, i, ref effectsAvailable);
            var slash = relative.LastIndexOf('/');
            var parent = slash > 0 && latest.TryGetValue(relative[..slash], out var p) ? p : root;
            parent.Children.Add(node);
            latest[relative] = node;
        }
        Sort(root);
        return root;
    }

    // Constraint: the layout Rect is a world-space box; the label lives under the node, so map it through the inverse world matrix.
    private static (double X, double Y, double W, double H)? LocalBox(SwfLayoutRowRecord row)
    {
        var w = row.WorldTransform;
        var det = w.ScaleX * w.ScaleY - w.RotateSkew0 * w.RotateSkew1;
        if (Math.Abs(det) < 1e-9)
            return null;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var r = row.Rect;
        foreach (var (cx, cy) in new[] { (r.X, r.Y), (r.X + r.Width, r.Y), (r.X, r.Y + r.Height), (r.X + r.Width, r.Y + r.Height) })
        {
            var dx = cx - w.TranslateX;
            var dy = cy - w.TranslateY;
            var x = (w.ScaleY * dx - w.RotateSkew1 * dy) / det;
            var y = (-w.RotateSkew0 * dx + w.ScaleX * dy) / det;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        return (minX, minY, maxX - minX, maxY - minY);
    }

    private static void Sort(SwfNode n)
    {
        n.Children.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        foreach (var c in n.Children)
            Sort(c);
    }

    private static List<SwfNode> ReadInstances(SwfFile file, SwfReadOptions options, List<string> warnings)
    {
        var list = new List<SwfNode>();
        var effectsAvailable = true;
        using var frames = file.OpenFrames();
        var instances = frames.Instances(0, 0);
        for (var i = 0; i < instances.Count; i++)
            list.Add(NodeOf(frames, 0, 0, i, instances[i], "", ref effectsAvailable));
        if (options.Settle)
            list = Settled(frames, list, ref effectsAvailable, warnings);
        foreach (var open in options.Open)
            OpenClip(frames, list, open, ref effectsAvailable, warnings);
        return list;
    }

    private static SwfNode NodeOf(SwfFrameSet frames, int timeline, int frame, int index, SwfFrameInstanceRecord ins, string prefix, ref bool effectsAvailable)
    {
        var t = ins.Transform;
        var node = new SwfNode(prefix + (ins.Path ?? ""), ins.Name ?? "", ins.Depth, ins.CharacterId, ins.IsSprite, ins.HasClipDepth ? ins.ClipDepth : (ushort?)null,
            new SwfMatrix(t.ScaleX, t.RotateSkew0, t.RotateSkew1, t.ScaleY, t.TranslateX, t.TranslateY), ins.Ratio);
        if (ins.HasBounds)
            node.Bounds = (ins.Bounds.X, ins.Bounds.Y, ins.Bounds.Width, ins.Bounds.Height);
        if (effectsAvailable)
            node.Effects = ReadEffects(frames, timeline, frame, index, ref effectsAvailable);
        return node;
    }

    private static Dictionary<ushort, (int Timeline, int Frames)> Timelines(SwfFrameSet frames)
    {
        var map = new Dictionary<ushort, (int, int)>();
        for (var i = 0; i < frames.TimelineCount; i++)
        {
            var (id, count) = frames.TimelineAt(i);
            if (id != 0)
                map[id] = (i, count);
        }
        return map;
    }

    // Constraint: a root-level sprite with its own timeline is an intro holder (ContainerMenu FilterHolder_mc); the movie rests on its last frame, so that frame replaces the holder's frame-0 children.
    private static List<SwfNode> Settled(SwfFrameSet frames, List<SwfNode> frame0, ref bool effectsAvailable, List<string> warnings)
    {
        var timelines = Timelines(frames);
        var result = new List<SwfNode>();
        string? skip = null;
        var settled = false;
        foreach (var n in frame0)
        {
            if (skip is not null && n.Path.StartsWith(skip, StringComparison.Ordinal))
                continue;
            result.Add(n);
            if (!n.IsSprite || n.Path.Contains('/') || n.Effects.HasColor || !timelines.TryGetValue(n.CharacterId, out var tl) || tl.Frames < 2)
                continue;
            skip = n.Path + "/";
            settled = true;
            var last = tl.Frames - 1;
            var inside = frames.Instances(tl.Timeline, last);
            for (var i = 0; i < inside.Count; i++)
                result.Add(NodeOf(frames, tl.Timeline, last, i, inside[i], skip, ref effectsAvailable));
        }
        if (!settled)
            warnings.Add("Settle found no root sprite with its own timeline; the intro state stays");
        return result;
    }

    // Constraint: the clip's own alpha is script state (QuantityMenu.OpenMenu sets 1), so its subtree takes the colour effects of its own timeline, which Radicle reports relative to the clip.
    private static void OpenClip(SwfFrameSet frames, List<SwfNode> list, string path, ref bool effectsAvailable, List<string> warnings)
    {
        var at = list.FindIndex(n => n.Path == path);
        if (at < 0 || !effectsAvailable || !Timelines(frames).TryGetValue(list[at].CharacterId, out var tl))
        {
            warnings.Add($"OpenClip {path}: clip, effects or timeline missing; the clip keeps its frame-0 alpha");
            return;
        }
        var inside = frames.Instances(tl.Timeline, 0);
        var prefix = path + "/";
        var span = new List<SwfNode>();
        for (var i = at + 1; i < list.Count && list[i].Path.StartsWith(prefix, StringComparison.Ordinal); i++)
            span.Add(list[i]);
        if (span.Count != inside.Count)
        {
            warnings.Add($"OpenClip {path}: {span.Count} read nodes against {inside.Count} timeline instances; the clip keeps its frame-0 alpha");
            return;
        }
        list[at].Effects = SwfEffects.None;
        for (var i = 0; i < span.Count; i++)
            span[i].Effects = ReadEffects(frames, tl.Timeline, 0, i, ref effectsAvailable);
    }

    // Constraint: the effects accessors need a Radicle build with them; an older native library lacks the entry points, so fall back to no effects.
    private static SwfEffects ReadEffects(SwfFrameSet frames, int timeline, int frame, int index, ref bool available)
    {
        try
        {
            var fx = frames.InstanceEffects(timeline, frame, index);
            var filters = new List<SwfFilter>();
            for (var f = 0; f < fx.FilterCount; f++)
            {
                try
                {
                    var ff = frames.InstanceFilter(timeline, frame, index, f);
                    filters.Add(new SwfFilter(ff.Id, ff.Inner, ff.Knockout, ff.Passes, ff.Color, ff.BlurX, ff.BlurY, ff.Angle, ff.Distance, ff.Strength));
                }
                catch (RadicleException)
                {
                    continue;
                }
            }
            return new SwfEffects(fx.HasColorTransform, fx.ColorMultiply.ToArray(), fx.ColorAdd.ToArray(), fx.HasBlendMode ? fx.BlendMode : null, filters);
        }
        catch (RadicleException)
        {
            return SwfEffects.None;
        }
        catch (EntryPointNotFoundException)
        {
            available = false;
            return SwfEffects.None;
        }
    }

    public static string PlainText(SwfLayoutTextRecord? t)
    {
        if (t is null)
            return "";
        var s = t.Html ? Regex.Replace(t.Text, "<[^>]+>", "") : t.Text;
        return s.Replace("&apos;", "'").Replace("&quot;", "\"").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
    }
}
