using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Quest;

namespace OpenCw.Papyrus.Tests;

internal static class CacheFixture
{
    internal const string DataFolder = "project-authored";

    internal static ScriptSourceCandidate Candidate(string name, byte[] pex) =>
        new(name, ScriptSourceKind.Loose, DataFolder, 0, pex.Length, () => pex);

    internal static ScriptSourceCandidate[] Candidates(params ScriptSpec[] scripts) =>
        ConformanceScripts.Compile(scripts.Select(script => (script.Name, script.Source)))
            .Select(compiled => Candidate(compiled.Key, compiled.Value)).ToArray();

    internal static ScriptCacheBuildResult Build(string root, IEnumerable<ScriptSourceCandidate> candidates,
        IReadOnlyList<QuestStartupInput>? questStartup = null) =>
        ScriptCacheBuilder.Build(candidates, DataFolder, root, questStartup: questStartup);

    internal static ScriptCacheBuildResult Build(string root, params ScriptSpec[] scripts) =>
        Build(root, Candidates(scripts));
}
