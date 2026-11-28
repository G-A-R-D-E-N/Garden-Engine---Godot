using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Service;

public static class ServiceDetailScroll
{
    public const int Step = 48;

    public static IReadOnlyList<MenuHint> Hints { get; } =
    [
        new MenuHint(MenuInputAction.Left, "Detail up"),
        new MenuHint(MenuInputAction.Right, "Detail down"),
    ];

    public static int Delta(MenuInputAction action) => action switch
    {
        MenuInputAction.Left => -Step,
        MenuInputAction.Right => Step,
        _ => 0,
    };
}
