using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Actors;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class WorkshopNpcScriptTests
{
    static ScriptAttachment Script(string name, params ScriptValue[] values) => new(name, values);

    static ScriptValue Flag(string name, string text) => new(name, ScriptValueKind.Bool, text, null);

    [Fact]
    public void NoWorkshopScriptIsNoSettler()
    {
        Assert.Equal(new WorkshopNpcFacts(false, false), WorkshopNpcScript.Read(new[] { Script("SomethingElse", Flag("bCommandable", "true")) }));
        Assert.Equal(new WorkshopNpcFacts(false, false), WorkshopNpcScript.Read(System.Array.Empty<ScriptAttachment>()));
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData("maybe", false)]
    public void CommandableFollowsTheBoolProperty(string text, bool expected)
    {
        var facts = WorkshopNpcScript.Read(new[] { Script("workshopnpcscript", Flag("BCOMMANDABLE", text)) });

        Assert.Equal(new WorkshopNpcFacts(true, expected), facts);
    }

    [Fact]
    public void AMissingOrNonBoolPropertyIsNotCommandable()
    {
        Assert.Equal(new WorkshopNpcFacts(true, false), WorkshopNpcScript.Read(new[] { Script(WorkshopNpcScript.ScriptName) }));
        var text = new ScriptValue("bCommandable", ScriptValueKind.String, "true", null);
        Assert.Equal(new WorkshopNpcFacts(true, false), WorkshopNpcScript.Read(new[] { Script(WorkshopNpcScript.ScriptName, text) }));
    }
}
