using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public enum SaveKind
{
    Manual,
    Quick,
    Auto,
}

public sealed record SaveSlotInfo(string Id, string Name, string Character, int Level, TimeSpan Playtime,
                                  string Location, DateTimeOffset Timestamp, string ThumbnailPath,
                                  SaveKind Kind = SaveKind.Manual);

public interface ISaveSource
{
    IReadOnlyList<SaveSlotInfo> List();

    bool Delete(string id);
}
