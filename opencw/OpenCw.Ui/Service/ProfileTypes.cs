using System;
using System.Collections.Generic;
using System.Text;

namespace OpenCommonwealth.Services.Ui.Service;

public sealed record ProfileInfo(string Id, string Name, bool IsDefault = false);

public sealed record ProfileResult(bool Ok, string Message, ProfileInfo? Profile = null);

public interface IProfileStore
{
    string ActiveId { get; }

    IReadOnlyList<ProfileInfo> List();

    IReadOnlyList<ProfileInfo> Deleted();

    ProfileResult Create(string name);

    ServiceResult Rename(string id, string name);

    ServiceResult Delete(string id);

    ServiceResult Restore(string id);

    ServiceResult Choose(string id);
}

public static class ProfileNames
{
    public const int MaxLength = 64;

    public static string NameHint => FormattableString.Invariant($"Profile names are plain text, 1 to {MaxLength} characters.");

    public static string Normalize(string? name) => (name ?? string.Empty).Trim();

    public static string? Check(string? name)
    {
        var text = Normalize(name);
        if (text.Length == 0) return "Enter a profile name.";
        var runes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune)) return "Names cannot contain control characters.";
            runes++;
        }
        return runes > MaxLength ? "Names are limited to " + MaxLength + " characters." : null;
    }
}
