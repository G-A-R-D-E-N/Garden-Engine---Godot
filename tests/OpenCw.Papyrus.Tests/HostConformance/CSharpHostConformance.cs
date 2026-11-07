using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCw.Papyrus.Tests;

public sealed class CSharpHostConformance : ScriptHostConformance
{
    protected override IScriptHost Create(IReadOnlyDictionary<string, byte[]> pex, EvidenceGate gate)
    {
        var library = new ScriptLibrary();
        foreach (var bytes in pex.Values)
        {
            foreach (var script in RadicleScriptLowering.Lower(bytes))
            {
                library.Add(script);
            }
        }
        return new ScriptHost(library, gate);
    }
}
