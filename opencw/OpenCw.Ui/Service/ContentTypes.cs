using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Service;

public enum ContentType
{
    Installed,
    CreationClub,
}

public sealed record ContentFile(string Path, long Bytes);

public sealed record ContentItem(
    string FileName,
    ContentType Type,
    bool Enabled,
    string Author,
    string Description,
    IReadOnlyList<ContentFile> Files,
    string Error = "")
{
    public long AssociatedBytes
    {
        get
        {
            long total = 0;
            foreach (var file in Files) total += file.Bytes;
            return total;
        }
    }
}

public interface IContentLibrary
{
    IReadOnlyList<ContentItem> Load();
}
