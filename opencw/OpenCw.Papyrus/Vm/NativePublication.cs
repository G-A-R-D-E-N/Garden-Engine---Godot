namespace OpenCommonwealth.Services.Papyrus.Vm;

public enum NativePublicationState
{
    Unregistered,
    Published,
    Pending,
    Failed,
}

public sealed record NativePublication(NativePublicationState State, string? Failure);
