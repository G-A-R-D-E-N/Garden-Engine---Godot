namespace OpenCommonwealth.Services.Ui.Flow;

public enum UiInputAction
{
    Pause,
    Gallery,
    PipBoy,
}

public enum UiRoute
{
    None,
    OpenPause,
    OpenGallery,
    OpenPipBoy,
    ClosePipBoy,
}

public readonly record struct UiInputContext(
    bool Headless,
    bool PlayerRegistered,
    bool MenuOpen,
    bool ModalOverlayOpen,
    bool LoadingVisible,
    bool Blocked,
    bool PipBoyOnTop)
{
    public bool Occupied => MenuOpen || ModalOverlayOpen || LoadingVisible || Blocked || PipBoyOnTop;
}

public static class UiInputRouter
{
    public static UiRoute Route(UiInputAction action, UiInputContext context)
    {
        if (context.Headless) return UiRoute.None;
        if (action == UiInputAction.PipBoy)
        {
            if (context.PipBoyOnTop) return context.ModalOverlayOpen ? UiRoute.None : UiRoute.ClosePipBoy;
            return context.PlayerRegistered && !context.Occupied ? UiRoute.OpenPipBoy : UiRoute.None;
        }
        if (context.Occupied) return UiRoute.None;
        return action == UiInputAction.Pause ? UiRoute.OpenPause : UiRoute.OpenGallery;
    }
}
