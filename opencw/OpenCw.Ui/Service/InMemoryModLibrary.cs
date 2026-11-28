using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Service;

public sealed class InMemoryModLibrary : IModLibrary
{
    public InMemoryModLibrary(IEnumerable<ModEntry> entries)
    {
        Entries = entries.ToList();
    }

    public List<ModEntry> Entries { get; }

    public bool FailApply { get; set; }

    public int ApplyCount { get; private set; }

    public IReadOnlyList<ModEntry>? Applied { get; private set; }

    public bool IsEnabled(string fileName) =>
        Entries.Any(e => e.FileName == fileName && e.Enabled);

    public IReadOnlyList<ModEntry> Load() => Entries.ToList();

    public IReadOnlyList<ModProblem> Validate(IReadOnlyList<ModEntry> order)
    {
        var problems = new List<ModProblem>();
        for (var i = 0; i < order.Count; i++)
        {
            var entry = order[i];
            if (!entry.Enabled) continue;
            if (entry.Missing)
                problems.Add(new ModProblem(entry.FileName, ProblemSeverity.Error, entry.FileName + " is enabled but the file is missing."));
            else if (entry.Error.Length > 0)
                problems.Add(new ModProblem(entry.FileName, ProblemSeverity.Error, entry.FileName + " cannot be read: " + entry.Error));
            foreach (var master in entry.Masters)
            {
                var at = order.ToList().FindIndex(e => e.FileName == master);
                if (at < 0 || !order[at].Enabled)
                    problems.Add(new ModProblem(entry.FileName, ProblemSeverity.Error, entry.FileName + " needs " + master + " enabled."));
                else if (at > i)
                    problems.Add(new ModProblem(entry.FileName, ProblemSeverity.Error, entry.FileName + " loads before its master " + master + "."));
            }
        }
        return problems;
    }

    public ServiceResult Apply(IReadOnlyList<ModEntry> order)
    {
        if (FailApply) return ServiceResult.Fail("The load order could not be written.");
        ApplyCount++;
        Applied = order.ToList();
        Entries.Clear();
        Entries.AddRange(order);
        return ServiceResult.Success();
    }
}
