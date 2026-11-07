using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OpenCommonwealth.Services.Papyrus.Coverage;

public sealed record CompatibilityReport(
    ClosureReport Closure,
    IReadOnlyList<string> MissingSystemBlockers,
    IReadOnlyList<string> OpenEvidenceKeys,
    bool Compatible,
    string Verdict)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"compatible={Compatible} missing-system-blockers={MissingSystemBlockers.Count} open-evidence-keys={OpenEvidenceKeys.Count} gaps={Closure.Gaps.Count}\n");
        text.Append(CultureInfo.InvariantCulture, $"verdict: {Verdict}\n");
        foreach (var blocker in MissingSystemBlockers)
        {
            text.Append(CultureInfo.InvariantCulture, $"  blocker {blocker}\n");
        }
        foreach (var key in OpenEvidenceKeys)
        {
            text.Append(CultureInfo.InvariantCulture, $"  open-key {key}\n");
        }
        text.Append(Closure.ToReport());
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// The full runtime compatibility gate.
/// </summary>
public static class RuntimeCompatibilityGate
{
    public static CompatibilityReport Run(string registryJson, IEnumerable<PartCTask> tasks)
    {
        var closure = PartCClosureGate.Run(registryJson, tasks);
        var blockers = closure.Rows
            .Where(row => row.State == ClosureState.MissingGardenSystem)
            .Select(row => row.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        var openKeys = closure.Rows
            .Where(row => row.State == ClosureState.OpenEvidenceKey)
            .Select(row => row.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var compatible = closure.Clean && blockers.Count == 0 && openKeys.Count == 0;
        var verdict = compatible
            ? "full runtime compatibility is claimed: the closure gate is clean, no missing Garden system blocks a script, and no open evidence keys remain"
            : $"no compatibility claim: {blockers.Count.ToString(CultureInfo.InvariantCulture)} missing Garden system(s), {openKeys.Count.ToString(CultureInfo.InvariantCulture)} open evidence key(s) and {closure.Gaps.Count.ToString(CultureInfo.InvariantCulture)} gap(s) remain, so attached scripts can still be stopped";

        return new CompatibilityReport(closure, blockers, openKeys, compatible, verdict);
    }
}
