using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettlerCommandTextTests
{
    [Fact]
    public void AnAssignmentWithNobodyDisplacedIsJustTheAssignedText()
    {
        Assert.Equal(SettlerCommandText.AssignedText, SettlerCommandText.Toast(new CommandResult(CommandOutcome.Assigned), _ => "Bob"));
    }

    [Fact]
    public void ADisplacedSettlerIsNamedInTheToast()
    {
        var text = SettlerCommandText.Toast(new CommandResult(CommandOutcome.Assigned, "key:bob"), key => key == "key:bob" ? "Bob" : null);

        Assert.Equal("Settler assigned. Bob was unassigned.", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ADisplacedSettlerThatCannotBeNamedGetsTheFallbackAndNeverItsKey(string? name)
    {
        var text = SettlerCommandText.Toast(new CommandResult(CommandOutcome.Assigned, "key:bob"), _ => name);

        Assert.Equal("Settler assigned. Another settler was unassigned.", text);
        Assert.DoesNotContain("key:bob", text);
    }

    [Fact]
    public void ARefusalSaysSoAndEveryOtherOutcomeIsSilent()
    {
        Assert.Equal(SettlerCommandText.RefusedText, SettlerCommandText.Toast(new CommandResult(CommandOutcome.Refused), _ => null));
        Assert.Null(SettlerCommandText.Toast(new CommandResult(CommandOutcome.Ended), _ => null));
        Assert.Null(SettlerCommandText.Toast(new CommandResult(CommandOutcome.Ignored), _ => null));
    }
}
