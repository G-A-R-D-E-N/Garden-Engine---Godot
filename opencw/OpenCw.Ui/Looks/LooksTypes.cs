using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Looks;

public enum LooksContext
{
    NewCharacter,
    Haircut,
    BodyMod,
}

public sealed record LooksOption(string Id, string Name, string Description = "");

public sealed record LooksCategory(string Id, string Name, IReadOnlyList<LooksOption> Options, string? SelectedId,
                                   bool Optional = false);

public sealed record LooksChoice(string CategoryId, string? OptionId);

public interface ILooksSession
{
    LooksContext Context { get; }

    bool IsValid { get; }

    IReadOnlyList<LooksCategory> Categories { get; }

    ActionResult Preview(string categoryId, string? optionId);

    void Commit(IReadOnlyList<LooksChoice> choices);

    void Cancel();
}
