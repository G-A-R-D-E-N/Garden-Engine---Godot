using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Layout;

public static class DefaultLayouts
{
    public const string State = "default";
    public const double StageWidth = 1280;
    public const double StageHeight = 720;

    private const uint White = 0xFFFFFFFF;

    private static readonly TextStyle HeadingStyle = new(null, 28, White, LayoutTextAlign.Center, false, false, false, "");
    private static readonly TextStyle BodyStyle = new(null, 20, White, LayoutTextAlign.Left, true, true, false, "");
    private static readonly TextStyle NoteStyle = new(null, 16, White, LayoutTextAlign.Left, false, false, false, "");

    public static MenuLayout MessageBox { get; } = Menu(
        "message_box",
        Shape("panel", 0, 320, 200, 640, 320),
        Text("title", 1, 352, 224, 576, 48, HeadingStyle),
        Text("body", 1, 352, 288, 576, 160, BodyStyle),
        Shape("confirm", 1, 352, 456, 270, 48),
        Shape("cancel", 1, 658, 456, 270, 48));

    public static MenuLayout ListMenu { get; } = Menu(
        "list_menu",
        Shape("panel", 0, 240, 120, 800, 480),
        Text("title", 1, 272, 144, 736, 48, HeadingStyle),
        Shape("list", 1, 272, 208, 736, 336),
        Text("hint", 1, 272, 560, 736, 24, NoteStyle));

    public static MenuLayout DetailPane { get; } = Menu(
        "detail_pane",
        Shape("panel", 0, 800, 40, 440, 640),
        Text("title", 1, 824, 64, 392, 48, HeadingStyle),
        Row("image", LayoutRowKind.Bitmap, 1, 824, 128, 392, 216, null),
        Text("body", 1, 824, 360, 392, 240, BodyStyle),
        Text("footer", 1, 824, 616, 392, 40, NoteStyle));

    public static IReadOnlyList<MenuLayout> All { get; } = [MessageBox, ListMenu, DetailPane];

    private static MenuLayout Menu(string menu, params LayoutRow[] rows) =>
        new(menu, State, StageWidth, StageHeight, [], rows);

    private static LayoutRow Shape(string path, int depth, double x, double y, double width, double height) =>
        Row(path, LayoutRowKind.Shape, depth, x, y, width, height, null);

    private static LayoutRow Text(string path, int depth, double x, double y, double width, double height, TextStyle style) =>
        Row(path, LayoutRowKind.Text, depth, x, y, width, height, style);

    private static LayoutRow Row(string path, LayoutRowKind kind, int depth, double x, double y, double width, double height, TextStyle? text) =>
        new(path, kind, depth, true, new LayoutRect(x, y, width, height), text);
}
