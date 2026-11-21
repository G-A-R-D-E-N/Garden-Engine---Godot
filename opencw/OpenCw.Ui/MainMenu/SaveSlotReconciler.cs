using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public sealed record SavePayload(string Id, DateTimeOffset Modified);

public static class SaveSlotReconciler
{
    // Requires: a slot is loadable only when its payload exists; a payload without a sidecar is listed with a made-up entry so a failed sidecar write does not hide the save.
    public static IReadOnlyList<SaveSlotInfo> Reconcile(IEnumerable<SaveSlotInfo> sidecars, IEnumerable<SavePayload> payloads, Func<string, SaveKind> kindOf)
    {
        var infos = new Dictionary<string, SaveSlotInfo>(StringComparer.Ordinal);
        foreach (var info in sidecars) infos[info.Id] = info;
        var slots = new List<SaveSlotInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var payload in payloads.Where(p => SaveInfoFile.IsValidId(p.Id)))
        {
            if (!seen.Add(payload.Id)) continue;
            slots.Add(infos.TryGetValue(payload.Id, out var info)
                ? info
                : new SaveSlotInfo(payload.Id, payload.Id, "", 0, TimeSpan.Zero, "", payload.Modified, "", kindOf(payload.Id)));
        }
        return slots;
    }
}

public interface ISaveFiles
{
    bool Exists(string path);

    bool Move(string from, string to);

    bool Remove(string path);
}

public static class SaveSlotDeleter
{
    public const string TrashSuffix = ".deleted";

    // Invariant: either every existing file of the slot is gone or none is; a failed move puts the already moved files back, and a trash file that cannot be removed afterwards is not a slot file.
    public static bool Delete(ISaveFiles files, IReadOnlyList<string> paths)
    {
        var moved = new List<string>();
        foreach (var path in paths.Where(files.Exists))
        {
            if (files.Move(path, path + TrashSuffix))
            {
                moved.Add(path);
                continue;
            }
            foreach (var back in moved) files.Move(back + TrashSuffix, back);
            return false;
        }
        if (moved.Count == 0) return false;
        foreach (var path in moved) files.Remove(path + TrashSuffix);
        return true;
    }
}
