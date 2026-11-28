using System.Collections.Generic;

namespace OpenCommonwealth.Services;

public readonly record struct RgbF(float R, float G, float B);

public sealed record MapMarkerInfo(string? Name, string Type, bool Visible, bool CanTravelTo);

public sealed record BoundsInfo(short[] Min, short[] Max);

public sealed record StandInInfo(int Version, double Radius);

public sealed record MaterialSubstitutionInfo(string Original, string? Replacement, float? ColorRemappingIndex);

public sealed record MaterialSwapInfo(string FormKey, IReadOnlyList<MaterialSubstitutionInfo>? Substitutions);

public sealed record SplineInfo(Vec3 End, float Slack, float Thickness);

public sealed record SplineDefInfo(int Slices, float R, float G, float B);

public sealed record ScolPlacement(Vec3 Position, Vec3 Rotation, float Scale);

public sealed record ScolPart(string ModelPath, MaterialSwapInfo? MaterialSwap, float? ColorRemappingIndex, float BoundsRadius,
    BoundsInfo? Bounds, bool DistantLod, string[]? LodMeshes, string? Foliage, StandInInfo? StandIn, IReadOnlyList<ScolPlacement> Placements);

public sealed record PlacedLight(float Radius, RgbF Color, float Fov, bool IsSpot, bool CastsShadow, bool AmbientOnly, bool OffByDefault,
    bool Flicker, bool Pulse, uint Flags, float Fade, float Constant, float Scalar, float Exponent, float FalloffExponent, float NearClip,
    float FlickerPeriod, float FlickerIntensity, float FlickerMovement, bool RandomAnimStart, string? Gobo);

public sealed record PlacedLayer(string FormKey, string? EditorId, string? Parent);

public sealed record PlacedLighting(RgbF AmbientColor, RgbF FogNearColor, RgbF FogFarColor, float FogNear, float FogFar, float? ImageSpaceEyeAdaptSpeed);

public sealed record PlacedRef
{
    public string FormKey { get; init; } = "";
    public string? EditorId { get; init; }
    public string RecordType { get; init; } = "";
    public string? BaseType { get; init; }
    public string BaseFormKey { get; init; } = "";
    public string? BaseEditorId { get; init; }
    public string? BaseName { get; init; }
    public MapMarkerInfo? MapMarker { get; init; }
    public TeleportInfo? Teleport { get; init; }
    public EnableParentInfo? EnableParent { get; init; }
    public bool InitiallyDisabled { get; init; }
    public string? EncounterZone { get; init; }
    public string? EncounterZoneEditorId { get; init; }
    public string? AttachRef { get; init; }
    public string? Comments { get; init; }
    public IReadOnlyList<ScriptEntry>? Scripts { get; init; }
    public bool? ScriptsUnsupported { get; init; }
    public IReadOnlyList<LinkedReference>? LinkedReferences { get; init; }
    public OwnershipInfo? Ownership { get; init; }
    public OwnershipInfo? CellOwnership { get; init; }
    public ActivateParentsInfo? ActivateParents { get; init; }
    public PatrolInfo? Patrol { get; init; }
    public RoomPortalInfo? RoomPortal { get; init; }
    public string? LocationReference { get; init; }
    public bool IgnoredBySandbox { get; init; }
    public bool OpenByDefault { get; init; }
    public uint Flags { get; init; }
    public IReadOnlyList<string> FlagNames { get; init; } = [];
    public IReadOnlyList<FlagDef> FlagTable { get; init; } = [];
    public bool? Respawns { get; init; }
    public IReadOnlyList<string>? LocationRefTypes { get; init; }
    public string? LevelModifier { get; init; }
    public int? Count { get; init; }
    public string? ModelPath { get; init; }
    public float BoundsRadius { get; init; }
    public BoundsInfo? Bounds { get; init; }
    public bool DistantLod { get; init; }
    public string[]? LodMeshes { get; init; }
    public bool LodProxy { get; init; }
    public bool EditorMarker { get; init; }
    public string? Foliage { get; init; }
    public bool HasPrimitive { get; init; }
    public string? WaterType { get; init; }
    public StandInInfo? StandIn { get; init; }
    public MaterialSwapInfo? ModelMaterialSwap { get; init; }
    public float? ModelColorRemappingIndex { get; init; }
    public MaterialSwapInfo? ReferenceMaterialSwap { get; init; }
    public string? DecalDiffuse { get; init; }
    public float? DecalWidth { get; init; }
    public float? DecalHeight { get; init; }
    public IReadOnlyList<ScolPart>? ScolParts { get; init; }
    public PlacedLight? Light { get; init; }
    public PrimitiveInfo? Primitive { get; init; }
    public SplineInfo? Spline { get; init; }
    public SplineDefInfo? SplineDef { get; init; }
    public Vec3 Position { get; init; }
    public Vec3 Rotation { get; init; }
    public float Scale { get; init; } = 1f;
    public string? Layer { get; init; }
    public string? LayerEditorId { get; init; }
}

public sealed record PlacedCell
{
    public string? Error { get; init; }
    public string CellFormKey { get; init; } = "";
    public string? CellEditorId { get; init; }
    public string? CellName { get; init; }
    public bool Interior { get; init; }
    public bool Persistent { get; init; }
    public string? WorldspaceFormKey { get; init; }
    public string? WorldspaceEditorId { get; init; }
    public string? WorldspaceName { get; init; }
    public int? GridX { get; init; }
    public int? GridY { get; init; }
    public PlacedLighting? Lighting { get; init; }
    public int WithModelCount { get; init; }
    public int UndecodableCount { get; init; }
    public IReadOnlyList<PlacedRef> References { get; init; } = [];
    public IReadOnlyList<PlacedLayer> Layers { get; init; } = [];
}
