using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public sealed class NamedSaveWriter : ISaveWriter
{
    public const string QuicksaveId = "quicksave";
    public const string GoneText = "That save is gone.";

    private readonly ISaveSource _saves;
    private readonly Func<string, bool> _write;

    public NamedSaveWriter(ISaveSource saves, Func<string, bool> write)
    {
        ArgumentNullException.ThrowIfNull(saves);
        ArgumentNullException.ThrowIfNull(write);
        _saves = saves;
        _write = write;
    }

    public static SaveListController Controller(ISaveSource saves, Func<Func<string, bool>?> current) =>
        new(saves, new NamedSaveWriter(saves, id => current()?.Invoke(id) ?? false), new SaveWriterGate(current));

    public ActionResult SaveNew()
    {
        var taken = _saves.List().Select(slot => slot.Id).ToHashSet(StringComparer.Ordinal);
        var number = 1;
        while (taken.Contains("save_" + number)) number++;
        return Write("save_" + number);
    }

    public ActionResult Overwrite(string id) =>
        _saves.List().Any(slot => slot.Id == id) ? Write(id) : new ActionResult(false, GoneText);

    public ActionResult Quicksave() => Write(QuicksaveId);

    private ActionResult Write(string id)
    {
        try
        {
            return _write(id) ? new ActionResult(true) : new ActionResult(false, SaveListController.FailedText);
        }
        catch (Exception e) when (e is InvalidOperationException or System.IO.IOException)
        {
            return new ActionResult(false, SaveListController.FailedText);
        }
    }
}
