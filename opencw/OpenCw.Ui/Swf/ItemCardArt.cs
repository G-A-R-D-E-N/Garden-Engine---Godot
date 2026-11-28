namespace OpenCommonwealth.Services.Ui.Swf;

public sealed record CardEntry(string Label, string Value);

public static class ItemCardArt
{
    public const string Entry = "card_std";
    public const string Symbol = "ItemCard_StandardEntry";
    public const string Clip = "FilterHolder_mc/Menu_mc/ItemCard_mc";
    public const double Height = 33.4;
    public const double Spacing = -3.5;
    public const double Scale = 0.7;
    public const double OriginX = 722;
    public const double OriginY = 484;
    public const double EntryWidth = 239.7;
    public const double NotesX = OriginX - EntryWidth * Scale;
    public const double NotesY = OriginY + 4;
    public const double NotesRight = 775;
    public const double NotesWidth = NotesRight - 8 - NotesX;

    public static IEnumerable<SwfTemplate> Templates()
    {
        yield return new SwfTemplate(Entry, Symbol, null, 0);
    }

    public static void Add(SwfBindings b, IReadOnlyList<CardEntry> entries)
    {
        var current = 0.0;
        foreach (var e in entries)
        {
            var y = (current - Height) * Scale;
            current -= Height + Spacing;
            b.Sprites.Add(new SwfSprite(Entry, Clip, 0, y, 1, Scale: Scale));
            b.Sprites.Add(new SwfSprite("", Clip, 0, y, 1, new SwfSpriteText(e.Label, -233.5, -0.3, 151, 34.65, 28, false, Align: SwfAlign.Left), Scale: Scale));
            b.Sprites.Add(new SwfSprite("", Clip, 0, y, 1, new SwfSpriteText(e.Value, -75.5, -0.3, 69, 33.6, 28, false, Align: SwfAlign.Right), Scale: Scale));
        }
    }
}
