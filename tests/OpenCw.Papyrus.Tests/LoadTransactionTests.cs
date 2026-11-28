using OpenCommonwealth.Services.Papyrus.Save;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class LoadTransactionTests
{
    [Fact]
    public void StepsRunInStageOrderWhateverTheRegistrationOrder()
    {
        var ran = new List<string>();
        var transaction = new LoadTransaction()
            .Add(LoadStage.Actors, "ledger", () => ran.Add("ledger"))
            .Add(LoadStage.Scripts, "instances", () => ran.Add("instances"))
            .Add(LoadStage.State, "globals", () => ran.Add("globals"))
            .Add(LoadStage.Quests, "stages", () => ran.Add("stages"))
            .Add(LoadStage.State, "perks", () => ran.Add("perks"));

        var result = transaction.Run();

        Assert.Equal(new[] { "stages", "instances", "globals", "perks", "ledger" }, ran);
        Assert.Equal(new[] { "Quests:stages", "Scripts:instances", "State:globals", "State:perks", "Actors:ledger" }, result.Ran);
        Assert.True(result.Succeeded);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void AFailingStageIsNamedAndAnIndependentLaterStageStillRuns()
    {
        var ran = new List<string>();
        var transaction = new LoadTransaction()
            .Add(LoadStage.Quests, "stages", () => throw new FormatException("quest 5 is not in its done set"))
            .Add(LoadStage.Scripts, "instances", () => ran.Add("instances"), null, LoadStage.Quests)
            .Add(LoadStage.Actors, "ledger", () => ran.Add("ledger"));

        var result = transaction.Run();

        var failure = Assert.Single(result.Failures);
        Assert.Equal((LoadStage.Quests, "stages", "quest 5 is not in its done set"), (failure.Stage, failure.Owner, failure.Reason));
        Assert.IsType<FormatException>(failure.Error);
        Assert.Equal("quests stage, stages: quest 5 is not in its done set", failure.Line);
        Assert.Equal(new[] { "ledger" }, ran);
        Assert.False(result.Succeeded);
        Assert.True(result.Failed(LoadStage.Quests));
        Assert.False(result.Failed(LoadStage.Actors));
    }

    [Fact]
    public void ADependentStepIsSkippedAndNamesTheStageAndOwnerThatFailed()
    {
        var transaction = new LoadTransaction()
            .Add(LoadStage.Quests, "stages", () => throw new InvalidOperationException("boom"))
            .Add(LoadStage.Scripts, "instances", () => throw new InvalidOperationException("must not run"), null, LoadStage.Quests)
            .Add(LoadStage.Actors, "scenes", () => throw new InvalidOperationException("must not run"), null, LoadStage.Quests, LoadStage.Scripts);

        var result = transaction.Run();

        Assert.Single(result.Failures);
        Assert.Equal(new[] { "instances", "scenes" }, result.Skipped.Select(issue => issue.Owner));
        Assert.All(result.Skipped, issue => Assert.Contains("the quests stage failed at stages", issue.Reason));
        Assert.Equal(new[] { "Quests:stages" }, result.Ran);
    }

    [Fact]
    public void IrreversibleStateAndActorStepsAreSkippedWhenQuestsOrScriptsFailedAndNothingOfTheirsRuns()
    {
        var ran = new List<string>();
        var transaction = new LoadTransaction()
            .Add(LoadStage.State, "globals", () => ran.Add("globals"), null, LoadStage.Quests, LoadStage.Scripts)
            .Add(LoadStage.Actors, "ledger", () => ran.Add("ledger"), null, LoadStage.Quests, LoadStage.Scripts)
            .Add(LoadStage.Scripts, "instances", () => throw new InvalidOperationException("refused"), null, LoadStage.Quests)
            .Add(LoadStage.Quests, "stages", () => ran.Add("stages"));

        var result = transaction.Run();

        Assert.Equal(new[] { "stages" }, ran);
        Assert.Equal(new[] { "globals", "ledger" }, result.Skipped.Select(issue => issue.Owner));
        Assert.All(result.Skipped, issue => Assert.Contains("the scripts stage failed at instances", issue.Reason));
    }

    [Fact]
    public void AStepThatNeedsItsOwnStageIsSkippedAfterAnEarlierStepOfThatStageFailed()
    {
        var ran = new List<string>();
        var transaction = new LoadTransaction()
            .Add(LoadStage.Scripts, "instances", () => throw new InvalidOperationException("refused"))
            .Add(LoadStage.Scripts, "properties", () => ran.Add("properties"), null, LoadStage.Scripts)
            .Add(LoadStage.Scripts, "others", () => ran.Add("others"));

        var result = transaction.Run();

        Assert.Equal(new[] { "others" }, ran);
        Assert.Equal("properties", Assert.Single(result.Skipped).Owner);
    }

    [Fact]
    public void AFailedStepRunsItsUndoWithTheErrorAndAFailingUndoIsRecordedOnTheError()
    {
        Exception? seen = null;
        var transaction = new LoadTransaction()
            .Add(LoadStage.Scripts, "instances", () => throw new FormatException("bad"), error => { seen = error; throw new InvalidOperationException("undo broke"); });

        var result = transaction.Run();

        var failure = Assert.Single(result.Failures);
        Assert.Same(failure.Error, seen);
        Assert.Equal("undo broke", failure.Error!.Data[LoadTransaction.UndoFailureKey]);
        Assert.Equal("bad", failure.Reason);
    }

    [Fact]
    public void ThrowFirstRethrowsTheOriginalExceptionAndDoesNothingOnSuccess()
    {
        var failed = new LoadTransaction().Add(LoadStage.Quests, "stages", () => throw new ArgumentException("bad")).Run();
        Assert.Throws<ArgumentException>(failed.ThrowFirst);

        new LoadTransaction().Add(LoadStage.Quests, "stages", () => { }).Run().ThrowFirst();
    }

    [Fact]
    public void AWarningIsReportedWithoutFailingTheLoad()
    {
        var transaction = new LoadTransaction();
        transaction.Add(LoadStage.Scripts, "references", () => transaction.Warn(LoadStage.Scripts, "references", "left unresolved: A -> B"));

        var result = transaction.Run();

        Assert.True(result.Succeeded);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("scripts stage, references: left unresolved: A -> B", warning.Line);
    }

    [Fact]
    public void AStepCannotNeedALaterStage() =>
        Assert.Throws<ArgumentException>(() => new LoadTransaction().Add(LoadStage.Quests, "stages", () => { }, null, LoadStage.Scripts));
}
