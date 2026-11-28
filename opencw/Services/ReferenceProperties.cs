using System.Collections.Generic;

namespace OpenCommonwealth.Services;

public sealed record ScriptProperty(string Name, string Type, string? Value, string? Unsupported = null,
    string? ElementType = null, IReadOnlyList<ScriptProperty>? Elements = null, IReadOnlyList<ScriptProperty>? Members = null, bool None = false);

public sealed record ScriptEntry(string Name, IReadOnlyList<ScriptProperty> Properties);

public sealed record FlagDef(string Name, int Bit);

public sealed record LinkedReference(string? Reference, string? ReferenceEditorId, string? Keyword, string? KeywordEditorId);

public sealed record OwnershipInfo(string? Owner, string? OwnerEditorId, bool NoCrime, int? FactionRank);

public sealed record ActivateParent(string? Reference, string? ReferenceEditorId, float Delay);

public sealed record ActivateParentsInfo(string? Flags, IReadOnlyList<ActivateParent> Parents);

public sealed record PatrolInfo(float IdleTime, string? Idle, string? Topic);

public sealed record EnableParentInfo(string? Reference, bool Opposite);

public sealed record TeleportInfo(string? Door, Vec3 Position, Vec3 Rotation);

public sealed record PrimitiveInfo(string? Type, Vec3 Bounds);

public sealed record PortalInfo(string? Origin, string? Destination);

public sealed record RoomPortalInfo(IReadOnlyList<string> LinkedRooms, int PortalCount, bool HasRoomPortal, string? LightingTemplate, string? ImageSpace,
    IReadOnlyList<string>? LinkedRoomKeys = null, IReadOnlyList<PortalInfo>? Portals = null);

public readonly record struct Vec3(float X, float Y, float Z)
{
    public override string ToString() => $"{X:0.###}, {Y:0.###}, {Z:0.###}";
}

public sealed record ReferenceProperties
{
    public string FormKey { get; init; } = "";
    public string? EditorId { get; init; }
    public string RecordType { get; init; } = "";

    public string? BaseFormKey { get; init; }
    public string? BaseEditorId { get; init; }
    public string? BaseName { get; init; }
    public string? BaseType { get; init; }

    public string? Layer { get; init; }
    public string? LayerEditorId { get; init; }
    public string? EncounterZone { get; init; }
    public string? EncounterZoneEditorId { get; init; }
    public string? AttachRef { get; init; }
    public string? Comments { get; init; }
    public IReadOnlyList<ScriptEntry> Scripts { get; init; } = [];
    public IReadOnlyList<LinkedReference> LinkedReferences { get; init; } = [];
    public IReadOnlyList<string> LocationRefTypes { get; init; } = [];
    public string? LocationReference { get; init; }

    public OwnershipInfo? Ownership { get; init; }
    public ActivateParentsInfo? ActivateParents { get; init; }
    public PatrolInfo? Patrol { get; init; }
    public EnableParentInfo? EnableParent { get; init; }
    public TeleportInfo? Teleport { get; init; }
    public PrimitiveInfo? Primitive { get; init; }
    public RoomPortalInfo? RoomPortal { get; init; }

    public Vec3 Position { get; init; }
    public Vec3 Rotation { get; init; }

    public uint Flags { get; init; }
    public IReadOnlyList<string> FlagNames { get; init; } = [];
    public IReadOnlyList<FlagDef> FlagTable { get; init; } = [];
    public bool? Respawns { get; init; }
    public bool IgnoredBySandbox { get; init; }
    public bool OpenByDefault { get; init; }
    public bool InitiallyDisabled { get; init; }

    public string? ModelPath { get; init; }

    public float Scale { get; init; } = 1f;

    public string DisplayName =>
        EditorId
        ?? BaseEditorId
        ?? BaseName
        ?? FormKey;

    public static ReferenceProperties From(PlacedRef r) => new()
    {
        FormKey = r.FormKey,
        EditorId = r.EditorId,
        RecordType = r.RecordType,
        BaseFormKey = r.BaseFormKey,
        BaseEditorId = r.BaseEditorId,
        BaseName = r.BaseName,
        BaseType = r.BaseType,
        Layer = r.Layer,
        LayerEditorId = r.LayerEditorId,
        EncounterZone = r.EncounterZone,
        EncounterZoneEditorId = r.EncounterZoneEditorId,
        AttachRef = r.AttachRef,
        Comments = r.Comments,
        Scripts = r.Scripts ?? [],
        LinkedReferences = r.LinkedReferences ?? [],
        Position = r.Position,
        Rotation = r.Rotation,
        LocationRefTypes = r.LocationRefTypes ?? [],
        LocationReference = r.LocationReference,
        Ownership = r.Ownership,
        ActivateParents = r.ActivateParents,
        Patrol = r.Patrol,
        EnableParent = r.EnableParent,
        Teleport = r.Teleport,
        Primitive = r.Primitive,
        RoomPortal = r.RoomPortal,
        Flags = r.Flags,
        FlagNames = r.FlagNames,
        FlagTable = r.FlagTable,
        Respawns = r.Respawns,
        IgnoredBySandbox = r.IgnoredBySandbox,
        OpenByDefault = r.OpenByDefault,
        InitiallyDisabled = r.InitiallyDisabled,
        ModelPath = r.ModelPath,
        Scale = r.Scale,
    };
}
