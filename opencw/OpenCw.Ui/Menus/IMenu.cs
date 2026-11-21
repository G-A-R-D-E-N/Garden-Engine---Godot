namespace OpenCommonwealth.Services.Ui.Menus;

public interface IMenu
{
    string Id { get; }

    bool IsModal => false;

    bool CloseOnBack => true;

    bool HandleAction(MenuInputAction action);

    void OnOpened()
    {
    }

    void OnCovered()
    {
    }

    void OnUncovered()
    {
    }

    void OnClosed()
    {
    }
}
