using System;
using System.Globalization;

namespace OpenCommonwealth.Services.Ui.Service;

public static class ServiceText
{
    public const string NotSupplied = "not supplied";

    public const string DialogBusy = "Another dialog is open. Try again.";

    public static string Bytes(long bytes)
    {
        if (bytes < 1024) return FormattableString.Invariant($"{Math.Max(0, bytes)} B");
        double value = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        var unit = -1;
        while ((unit < 0 || Math.Round(value, 1) >= 1024) && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    public static string OrNotSupplied(string? text) => string.IsNullOrWhiteSpace(text) ? NotSupplied : text;

    public static string KindLabel(ModKind kind) => kind switch
    {
        ModKind.Master => "ESM",
        ModKind.Light => "ESL",
        _ => "ESP",
    };

    public static string TypeLabel(ContentType type) => type switch
    {
        ContentType.CreationClub => "Creation Club (manifest)",
        _ => "Installed plugin",
    };
}
