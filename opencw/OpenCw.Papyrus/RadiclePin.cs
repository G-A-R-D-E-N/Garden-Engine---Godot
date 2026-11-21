namespace OpenCommonwealth.Services.Papyrus;

/// <summary>
/// The exact Radicle revision the Papyrus runtime is built against.
/// </summary>
// Invariant: the pin names the revision and C ABI that produced the script cache metadata; Radicle.Interop is referenced through RadicleSourceDir.
public static class RadiclePin
{
    public const string Repository = "G-A-R-D-E-N/GardenTools";
    public const string Branch = "nomad/dev";
    public const string Commit = "822ba5a759a486ad9289e97fe3632dfe7a4568df";
    public const int AbiVersion = 700622;

    public static string Describe() => $"{Repository}@{Commit[..12]} ABI {AbiVersion}";
}
