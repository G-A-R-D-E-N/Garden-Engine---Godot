using System.Text.RegularExpressions;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Swf;

public readonly record struct SwfMatrix(double A, double B, double C, double D, double Tx, double Ty);

public sealed class SwfNode
{
    public SwfNode(string path, string name, int depth, ushort characterId, bool isSprite, ushort? clipDepth, SwfMatrix local)
    {
        Path = path;
        Name = name;
        Depth = depth;
        CharacterId = characterId;
        IsSprite = isSprite;
        ClipDepth = clipDepth;
        Local = local;
    }

    public string Path { get; }
    public string Name { get; }
    public int Depth { get; }
    public ushort CharacterId { get; }
    public bool IsSprite { get; }
    public ushort? ClipDepth { get; }
    public bool IsMask => ClipDepth.HasValue;
    public SwfMatrix Local { get; }
    public bool Visible { get; set; } = true;
    public (double X, double Y, double W, double H)? Bounds { get; set; }
    public SwfLayoutTextRecord? Text { get; set; }
    public SwfEffects Effects { get; set; } = SwfEffects.None;
    public List<SwfNode> Children { get; } = new();

    public static List<SwfNode> MaskedBy(SwfNode mask, IEnumerable<SwfNode> siblings) =>
        mask.ClipDepth is { } end ? siblings.Where(s => s.Depth > mask.Depth && s.Depth <= end).ToList() : new List<SwfNode>();
}

public sealed class SwfStage : IDisposable
{
    private readonly SwfFile _file;
    private readonly Dictionary<ushort, string?> _svg = new();

    private SwfStage(SwfFile file, SwfNode root, double width, double height)
    {
        _file = file;
        Root = root;
        Width = width;
        Height = height;
    }

    public SwfNode Root { get; }
    public double Width { get; }
    public double Height { get; }

    public string? ShapeSvg(ushort characterId)
    {
        if (_svg.TryGetValue(characterId, out var cached))
            return cached;
        string? svg;
        try { svg = _file.ShapeSvg(characterId); }
        catch (Exception) { svg = null; }
        _svg[characterId] = svg;
        return svg;
    }

    public void Dispose() => _file.Dispose();

    public static SwfStage Read(byte[] bytes)
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
            foreach (var n in ReadInstances(file))
            {
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
            return new SwfStage(file, root, layout.Stage.Width, layout.Stage.Height);
        }
        catch
        {
            file.Dispose();
            throw;
        }
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

    private static List<SwfNode> ReadInstances(SwfFile file)
    {
        var list = new List<SwfNode>();
        var effectsAvailable = true;
        using var frames = file.OpenFrames();
        var instances = frames.Instances(0, 0);
        for (var i = 0; i < instances.Count; i++)
        {
            var ins = instances[i];
            var t = ins.Transform;
            var node = new SwfNode(ins.Path ?? "", ins.Name ?? "", ins.Depth, ins.CharacterId, ins.IsSprite, ins.HasClipDepth ? ins.ClipDepth : (ushort?)null,
                new SwfMatrix(t.ScaleX, t.RotateSkew0, t.RotateSkew1, t.ScaleY, t.TranslateX, t.TranslateY));
            if (ins.HasBounds)
                node.Bounds = (ins.Bounds.X, ins.Bounds.Y, ins.Bounds.Width, ins.Bounds.Height);
            if (effectsAvailable)
                node.Effects = ReadEffects(frames, i, ref effectsAvailable);
            list.Add(node);
        }
        return list;
    }

    // Constraint: the effects accessors need a Radicle build with them; an older native library lacks the entry points, so fall back to no effects.
    private static SwfEffects ReadEffects(SwfFrameSet frames, int index, ref bool available)
    {
        try
        {
            var fx = frames.InstanceEffects(0, 0, index);
            var filters = new List<SwfFilter>();
            for (var f = 0; f < fx.FilterCount; f++)
            {
                try
                {
                    var ff = frames.InstanceFilter(0, 0, index, f);
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
