using System.Collections.Generic;

namespace OpenCommonwealth.Services.Interaction;

// Invariant: InOrder is the dispatch priority; the first handler that accepts the target runs, and Settler (when set) is asked before all others.
public sealed record ActivationHandlers(
    IActivationHandler Pickup, IActivationHandler PowerArmor, IActivationHandler Container, IActivationHandler Door,
    IActivationHandler Terminal, IActivationHandler Furniture, IActivationHandler Activator, IActivationHandler ActorLoot,
    IActivationHandler Pickpocket, IActivationHandler CatchAll)
{
    public IActivationHandler? Settler { get; init; }

    public IReadOnlyList<IActivationHandler> InOrder()
    {
        IActivationHandler[] rest = [Pickup, PowerArmor, Container, Door, Terminal, Furniture, Activator, ActorLoot, Pickpocket, CatchAll];
        if (Settler is not { } first) return rest;
        return [first, .. rest];
    }
}
