namespace OpenCommonwealth.Services.Ui.Terminals;

public static class TerminalRows
{
    public static bool CanReuse(int shownTop, int shownCount, int top, int count) =>
        shownTop == top && shownCount == count;
}
