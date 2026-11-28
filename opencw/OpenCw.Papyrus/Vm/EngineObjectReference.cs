using OpenCommonwealth.Services.Papyrus.Owners;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed record EngineObjectReference(string Identity)
{
    public EngineObjectReference(PapyrusOwnerId owner) : this(owner.Key) => Owner = owner;

    public PapyrusOwnerId? Owner { get; }

    // Invariant: the most derived script type the record is known to have; Radicle refuses a downcast from any other typed handle.
    public string? ScriptType { get; init; }
}
