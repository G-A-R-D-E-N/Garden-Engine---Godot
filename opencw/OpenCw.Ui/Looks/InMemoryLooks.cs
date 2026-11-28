using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Looks;

public sealed class InMemoryLooks : ILooksSession
{
    private readonly List<LooksCategory> _categories;

    public InMemoryLooks(LooksContext context = LooksContext.NewCharacter, IEnumerable<LooksCategory>? categories = null)
    {
        Context = context;
        _categories = (categories ?? Sample()).ToList();
    }

    public LooksContext Context { get; }

    public bool IsValid { get; set; } = true;

    public IReadOnlyList<LooksCategory> Categories => _categories;

    public HashSet<string> RefusedOptions { get; } = new();

    public List<(string CategoryId, string? OptionId)> Previews { get; } = new();

    public List<LooksChoice> Committed { get; } = new();

    public int Cancels { get; private set; }

    public static IEnumerable<LooksCategory> Sample() => new[]
    {
        new LooksCategory("hair_style", "Hair style", Options("hair", 4), "hair1"),
        new LooksCategory("hair_color", "Hair colour", Options("tint", 3), "tint1"),
        new LooksCategory("facial_hair", "Facial hair", Options("beard", 3), null, true),
        new LooksCategory("eyes", "Eyes", Options("eyes", 3), "eyes1"),
    };

    public ActionResult Preview(string categoryId, string? optionId)
    {
        if (optionId is not null && RefusedOptions.Contains(optionId))
            return new ActionResult(false, "That option cannot be shown.");
        Previews.Add((categoryId, optionId));
        return new ActionResult(true);
    }

    public void Commit(IReadOnlyList<LooksChoice> choices)
    {
        Committed.Clear();
        Committed.AddRange(choices);
    }

    public void Cancel() => Cancels++;

    private static LooksOption[] Options(string prefix, int count) =>
        Enumerable.Range(1, count)
            .Select(i => new LooksOption(prefix + i, "Style " + i, "Sample description " + i + "."))
            .ToArray();
}
