using System;
using System.Globalization;

namespace OpenCommonwealth.Services.Ui.Records;

public static class GmstSettings
{
    public const string Signature = "GMST";
    public const string FloatPath = "GMST\\FloatValue";
    public const string IntegerPath = "GMST\\IntegerValue";
    public const string StringPath = "GMST\\StringValue";

    // Invariant: an absent record, unreadable row, non-finite text or reader exception is the fallback.
    public static float Float(IRecordReader reader, string editorId, float fallback)
    {
        try
        {
            return reader.FindEditorId(editorId, Signature) is { } id
                && float.TryParse(reader.Scalar(id, FloatPath), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && float.IsFinite(value) ? value : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    // Invariant: an absent record or an unparsable value is null; there is no fallback number, and a reader exception propagates.
    public static int? Int(IRecordReader reader, string editorId) =>
        reader.FindEditorId(editorId, Signature) is { } id
        && int.TryParse(reader.Scalar(id, IntegerPath), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    // Invariant: string-table text with trailing NULs and whitespace trimmed; null for an absent record or a reader exception.
    public static string? Text(IRecordReader reader, string editorId)
    {
        try
        {
            return reader.FindEditorId(editorId, Signature) is { } id ? reader.Text(id, StringPath)?.TrimEnd((char)0).Trim() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
