using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

public enum TradeHeading
{
    ContainerPlayer,
    BarterPlayer,
    BarterVendor,
}

public sealed class TradeHeadings
{
    public const string StringPath = "GMST\\StringValue";
    public const string YourItems = "Your items";
    public const string VendorItems = "Vendor items";

    // Invariant: an id here is confirmed from the vanilla data to hold that heading's text; empty until then (see docs/code-notes/l14-ge-g3.md).
    public static readonly IReadOnlyDictionary<TradeHeading, string> VerifiedEditorIds = new Dictionary<TradeHeading, string>();

    private readonly IRecordReader? _reader;
    private readonly IReadOnlyDictionary<TradeHeading, string> _editorIds;

    public TradeHeadings(IRecordReader? reader, IReadOnlyDictionary<TradeHeading, string>? editorIds = null)
    {
        _reader = reader;
        _editorIds = editorIds ?? VerifiedEditorIds;
    }

    public static string Fallback(TradeHeading heading) => heading switch
    {
        TradeHeading.BarterVendor => VendorItems,
        _ => YourItems,
    };

    public string Text(TradeHeading heading)
    {
        if (_reader is not null && _editorIds.TryGetValue(heading, out var editorId) && Read(editorId) is { Length: > 0 } text)
            return text;
        return Fallback(heading);
    }

    public Func<string?> Source(TradeHeading heading) => () => Text(heading);

    private string? Read(string editorId)
    {
        try
        {
            return _reader!.FindEditorId(editorId, "GMST") is { } id ? _reader.Text(id, StringPath)?.TrimEnd((char)0).Trim() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
