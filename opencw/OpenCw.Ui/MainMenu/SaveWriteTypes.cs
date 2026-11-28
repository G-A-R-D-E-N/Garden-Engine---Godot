using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public interface ICanSaveQuery
{
    bool CanSave(out string reason);
}

public interface ISaveWriter
{
    ActionResult SaveNew();

    ActionResult Overwrite(string id);

    ActionResult Quicksave();
}
