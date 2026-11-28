using System;
using Newtonsoft.Json.Linq;

namespace OpenCommonwealth.Services;

public static class ManifestCellPolicy
{
    public enum Metadata { Fresh, Previous, Cleared }

    public readonly record struct Decision(string? Scene, Metadata Metadata);

    public static bool IsSameCell(string? previousFormKey, string? previousScene, string formKey, string? scene) =>
        scene != null && previousScene != null
        && string.Equals(previousFormKey, formKey, StringComparison.OrdinalIgnoreCase)
        && string.Equals(previousScene, scene, StringComparison.Ordinal);

    public static Decision Decide(string? scene, bool preservePrevious, bool sourceReadFailed, bool sameCell)
    {
        if (preservePrevious && sameCell) return new Decision(scene, Metadata.Previous);
        if (sourceReadFailed) return new Decision(null, Metadata.Cleared);
        return new Decision(scene, Metadata.Fresh);
    }

    public readonly record struct PublishedCounts(bool HasLand, int RefCount);

    public static PublishedCounts PublishCounts(Metadata kind, bool cellHasLand, int cellRefCount, bool? previousHasLand, int? previousRefCount)
    {
        if (kind == Metadata.Previous)
            return new PublishedCounts(previousHasLand ?? cellHasLand, previousRefCount ?? cellRefCount);
        if (kind == Metadata.Cleared)
            return new PublishedCounts(false, 0);
        return new PublishedCounts(cellHasLand, cellRefCount);
    }

    public readonly record struct Published(bool SameCell, string? Scene, PublishedCounts Counts);

    public static Published Resolve(JObject? old, string formKey, string? scene, bool preservePrevious, bool sourceReadFailed, bool hasLand, int refCount)
    {
        bool sameCell = old != null && IsSameCell((string?)old["formKey"], (string?)old["scene"], formKey, scene);
        var decision = Decide(scene, preservePrevious, sourceReadFailed, sameCell);
        var counts = PublishCounts(decision.Metadata, hasLand, refCount,
            old?["hasLand"]?.Type == JTokenType.Boolean ? (bool?)old["hasLand"] : null,
            old?["refCount"]?.Type == JTokenType.Integer ? (int?)old["refCount"] : null);
        return new Published(sameCell, decision.Scene, counts);
    }
}
