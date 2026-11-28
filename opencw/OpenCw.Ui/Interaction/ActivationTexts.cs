using System.Text;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Interaction;

public static class ActivationTexts
{
    public const string BaseType = "Activator";
    public const string RecordSignature = "ACTI";
    public const string TextPath = "ACTI\\ActivateText";

    public static string? Read(RadicleEnvironment env, StringTables strings, Record record)
    {
        try
        {
            if (env.Info(record).Signature != RecordSignature) return null;
            return Clean(strings.RecordText(record, TextPath));
        }
        catch (Exception ex) when (ex is RadicleException or ArgumentException or InvalidOperationException or DecoderFallbackException)
        {
            return null;
        }
    }

    public static string? Clean(string? text)
    {
        var trimmed = text?.TrimEnd((char)0).Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
