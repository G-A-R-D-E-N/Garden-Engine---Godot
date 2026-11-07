using OpenCommonwealth.Services.Papyrus.Acceptance;

namespace OpenCommonwealth.Services.Papyrus;

/// <summary>
/// The exact Radicle revision the Papyrus runtime is built against.
/// </summary>
/// <remarks>
/// Radicle is the only Bethesda binary parser, so the script cache records which revision and
/// which C ABI version produced its metadata. A pin mismatch is then a recorded fact instead of a
/// runtime surprise. The managed binding lives in GARDEN-TOOLS and is not a consumable dependency
/// of this project yet; docs/proof/papyrus-script-cache.md records that boundary.
/// </remarks>
public static class RadiclePin
{
    public const string Repository = "GARDEN/GARDEN-TOOLS";
    public const string Branch = "nomad/dev";
    public const string Commit = "9e77d132a4d276b8e9ebb8b054f5b0384d15ca76";
    public const int AbiVersion = 432;

    /// <summary>Where <see cref="Commit"/> is reachable from, as last measured against GARDEN-TOOLS.</summary>
    public const PinReachability Reachability = PinReachability.NomadDev;
    public const string BindingProject = "Radicle/bindings/csharp/Radicle.Interop/Radicle.Interop.csproj";

    public static string Describe() => $"{Repository}@{Commit[..12]} ABI {AbiVersion}";
}
