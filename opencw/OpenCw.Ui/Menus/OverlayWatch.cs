namespace OpenCommonwealth.Services.Ui.Menus;

public sealed class OverlayWatch
{
    private bool _open;

    public bool Changed(bool open)
    {
        if (open == _open) return false;
        _open = open;
        return true;
    }
}
