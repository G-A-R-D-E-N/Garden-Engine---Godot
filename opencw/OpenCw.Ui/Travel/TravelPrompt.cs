using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Workshop;

namespace OpenCommonwealth.Services.Ui.Travel;

public interface ITravelDestinations
{
    string Prompt { get; }

    IReadOnlyList<DestinationEntry> Destinations { get; }

    string? HighlightedId { get; }

    bool IsValid { get; }

    ActionResult Travel(string destinationId);
}

public static class TravelPrompt
{
    public const string Header = "Travel";
    public const string Question = "Travel to {name}?";

    public static DestinationPickerController? Create(ITravelDestinations? source) =>
        source is null
            ? null
            : DestinationPickerController.TryCreate(Header, source.Prompt.Length > 0 ? source.Prompt : Question,
                source.Destinations, source.HighlightedId, () => source.IsValid, source.Travel);
}
