using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public sealed class InMemorySaveStore : ISaveSource, ISaveWriter
{
    public const string QuicksaveId = "quicksave";

    private readonly List<SaveSlotInfo> _slots;
    private readonly Func<DateTimeOffset> _clock;

    public InMemorySaveStore(IEnumerable<SaveSlotInfo>? slots = null, Func<DateTimeOffset>? clock = null)
    {
        _slots = slots?.ToList() ?? new List<SaveSlotInfo>();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string? FailNext { get; set; }

    public int WriteCount { get; private set; }

    public IReadOnlyList<SaveSlotInfo> List() => _slots.ToList();

    public bool Delete(string id) => _slots.RemoveAll(slot => slot.Id == id) > 0;

    public ActionResult SaveNew()
    {
        if (Failure() is { } failed) return failed;
        var number = 1;
        while (_slots.Any(slot => slot.Id == "save_" + number)) number++;
        _slots.Add(Fresh("save_" + number, "Save " + number, SaveKind.Manual));
        return Wrote();
    }

    public ActionResult Overwrite(string id)
    {
        if (Failure() is { } failed) return failed;
        var index = _slots.FindIndex(slot => slot.Id == id);
        if (index < 0) return new ActionResult(false, "That save is gone.");
        _slots[index] = _slots[index] with { Timestamp = _clock() };
        return Wrote();
    }

    public ActionResult Quicksave()
    {
        if (Failure() is { } failed) return failed;
        _slots.RemoveAll(slot => slot.Id == QuicksaveId);
        _slots.Add(Fresh(QuicksaveId, "Quick save", SaveKind.Quick));
        return Wrote();
    }

    private ActionResult? Failure()
    {
        if (FailNext is null) return null;
        var reason = FailNext;
        FailNext = null;
        return new ActionResult(false, reason);
    }

    private ActionResult Wrote()
    {
        WriteCount++;
        return new ActionResult(true);
    }

    private SaveSlotInfo Fresh(string id, string name, SaveKind kind) =>
        new(id, name, "Character", 1, TimeSpan.Zero, string.Empty, _clock(), string.Empty, kind);
}

public sealed class SettableCanSave : ICanSaveQuery
{
    public bool Allowed { get; set; } = true;

    public string Reason { get; set; } = string.Empty;

    public bool CanSave(out string reason)
    {
        reason = Allowed ? string.Empty : Reason;
        return Allowed;
    }
}
