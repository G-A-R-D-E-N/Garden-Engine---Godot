using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Actors;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class CompanionNpcScriptTests
{
    static ScriptAttachment Script(string name) => new(name, System.Array.Empty<ScriptValue>());

    [Fact]
    public void A_marker_without_the_companion_script_is_not_a_possible_companion()
    {
        Assert.False(CompanionNpcScript.IsPossibleCompanion(System.Array.Empty<ScriptAttachment>()));
        Assert.False(CompanionNpcScript.IsPossibleCompanion(new[] { Script("WorkshopNPCScript") }));
    }

    [Fact]
    public void The_companion_script_is_matched_by_name_in_any_case_among_other_scripts()
    {
        Assert.True(CompanionNpcScript.IsPossibleCompanion(new[] { Script("WorkshopNPCScript"), Script("companionactorscript") }));
    }
}
