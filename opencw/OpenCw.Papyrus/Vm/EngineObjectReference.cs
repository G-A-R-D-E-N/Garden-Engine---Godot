using OpenCommonwealth.Services.Papyrus.Owners;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed record EngineObjectReference(string Identity)
{
    public EngineObjectReference(PapyrusOwnerId owner) : this(owner.Key) => Owner = owner;

    public PapyrusOwnerId? Owner { get; }
}
