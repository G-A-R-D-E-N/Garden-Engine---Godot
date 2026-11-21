using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ScriptOverrideRegistryTests
{
    private static readonly PapyrusValue[] NoArguments = Array.Empty<PapyrusValue>();

    private static EvidenceGate Gate(params (string Key, string Status)[] rows) =>
        new(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}"))}]}}"));

    private static ScriptOverrideRegistry Registry() => new(Gate(
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY")));

    private static ScriptInstance Instance(string script, string function, int? declared) =>
        new(script, name => name == function, name => name == function ? declared : null,
            1, new Dictionary<string, PapyrusValue>(), null);

    [Fact]
    public void TheResultFactoriesCarryTheirOutcomeValueAndReason()
    {
        Assert.Equal(new OverrideResult(OverrideOutcome.NotRegistered, PapyrusValue.None, null), OverrideResult.NotRegistered);
        Assert.Equal(new OverrideResult(OverrideOutcome.Declined, PapyrusValue.None, null), OverrideResult.Declined());
        Assert.Equal(new OverrideResult(OverrideOutcome.Declined, PapyrusValue.None, "why"), OverrideResult.Declined("why"));
        Assert.Equal(new OverrideResult(OverrideOutcome.Refused, PapyrusValue.None, "no"), OverrideResult.Refused("no"));
        var handled = OverrideResult.Handled(PapyrusValue.FromInt(9));
        Assert.Equal(OverrideOutcome.Handled, handled.Outcome);
        Assert.Equal(9, handled.Value.Integer);
        Assert.Null(handled.Reason);
    }

    [Theory]
    [InlineData("", "f")]
    [InlineData("  ", "f")]
    [InlineData("s", "")]
    [InlineData("s", " ")]
    public void ABlankScriptOrFunctionIsRejected(string script, string function)
    {
        var registry = Registry();

        var error = Assert.Throws<ArgumentException>(() => registry.Register(script, function, 0, _ => OverrideResult.Declined()));

        Assert.Equal("an override needs a script and a function", error.Message);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void ARegistrationReplacesAnEarlierOneForTheSameNamesIgnoringCaseAndIsAnnounced()
    {
        var registry = Registry();
        var announced = new List<(string, string, int)>();
        registry.Registered += (script, function, count) => announced.Add((script, function, count));

        registry.Register("Foo", "Bar", 1, _ => OverrideResult.Declined());
        registry.Register("FOO", "bar", ScriptOverrideRegistry.AnyParameterCount, _ => OverrideResult.Declined());

        Assert.Equal(1, registry.Count);
        Assert.Equal(new[] { ("FOO", "bar", -1) }, registry.Entries);
        Assert.Equal(new[] { ("Foo", "Bar", 1), ("FOO", "bar", -1) }, announced);
    }

    [Fact]
    public void AKeyThatIsNotInTheRegistryStillAllowsRegistrationAndCountsAStop()
    {
        var gate = Gate(("other", "PROVEN"));
        var registry = new ScriptOverrideRegistry(gate);

        registry.Register("S", "F", 0, _ => OverrideResult.Declined());

        Assert.Equal(1, registry.Count);
        Assert.Equal(1, gate.StopCount);
        Assert.Equal(1, gate.Stops[ScriptOverrideRegistry.Key]);
    }

    [Fact]
    public void ACallWithNoRegisteredOverrideIsNotRegisteredAndCountsNothing()
    {
        var registry = Registry();
        registry.Register("S", "Other", 0, _ => OverrideResult.Handled(PapyrusValue.None));

        var result = registry.TryInvoke(Instance("S", "F", 0), "F", NoArguments);

        Assert.Equal(OverrideResult.NotRegistered, result);
        Assert.Equal((0, 0, 0), (registry.HandledCount, registry.DeclinedCount, registry.RefusedCount));
    }

    [Fact]
    public void AHandledCallPassesTheInstanceFunctionAndArgumentsAndIsCounted()
    {
        var registry = Registry();
        OverrideContext? seen = null;
        registry.Register("Script", "Run", 1, context =>
        {
            seen = context;
            return OverrideResult.Handled(PapyrusValue.FromInt(5));
        });
        var instance = Instance("SCRIPT", "RUN", 1);
        var arguments = new[] { PapyrusValue.FromString("a") };

        var result = registry.TryInvoke(instance, "RUN", arguments);

        Assert.Equal(5, result.Value.Integer);
        Assert.Same(instance, seen!.Instance);
        Assert.Equal("RUN", seen.Function);
        Assert.Same(arguments, seen.Arguments);
        Assert.Equal((1, 0, 0), (registry.HandledCount, registry.DeclinedCount, registry.RefusedCount));
    }

    [Fact]
    public void ADeclinedCallIsCountedAndKeepsItsReason()
    {
        var registry = Registry();
        registry.Register("S", "F", 0, _ => OverrideResult.Declined("later"));

        var result = registry.TryInvoke(Instance("S", "F", 0), "F", NoArguments);

        Assert.Equal(OverrideResult.Declined("later"), result);
        Assert.Equal((0, 1, 0), (registry.HandledCount, registry.DeclinedCount, registry.RefusedCount));
    }

    [Fact]
    public void AHandlerThatReturnsRefusedOrNotRegisteredIsCountedAsRefused()
    {
        var registry = Registry();
        registry.Register("S", "A", 0, _ => OverrideResult.Refused("no"));
        registry.Register("S", "B", 0, _ => OverrideResult.NotRegistered);
        var instance = new ScriptInstance("S", _ => true, _ => 0, 1, new Dictionary<string, PapyrusValue>(), null);

        Assert.Equal(OverrideResult.Refused("no"), registry.TryInvoke(instance, "A", NoArguments));
        Assert.Equal(OverrideResult.NotRegistered, registry.TryInvoke(instance, "B", NoArguments));

        Assert.Equal((0, 0, 2), (registry.HandledCount, registry.DeclinedCount, registry.RefusedCount));
    }

    [Fact]
    public void AFunctionTheScriptDoesNotDefineIsRefusedBeforeTheHandlerRuns()
    {
        var registry = Registry();
        var ran = false;
        registry.Register("S", "Missing", 0, _ =>
        {
            ran = true;
            return OverrideResult.Handled(PapyrusValue.None);
        });

        var result = registry.TryInvoke(Instance("S", "F", 0), "Missing", NoArguments);

        Assert.False(ran);
        Assert.Equal(OverrideResult.Refused("the script S does not define Missing, so an override must not run"), result);
        Assert.Equal(1, registry.RefusedCount);
    }

    [Theory]
    [InlineData(2, 2, 1, "the override for F requires 2 declared arguments and a compatible registered count; received 1")]
    [InlineData(null, 1, 0, "the override for F requires an unknown number of declared arguments and a compatible registered count; received 0")]
    [InlineData(1, 2, 1, "the override for F requires 1 declared arguments and a compatible registered count; received 1")]
    public void AnArgumentCountThatDisagreesWithTheDeclaredOrRegisteredCountIsRefused(int? declared, int registered, int received, string reason)
    {
        var registry = Registry();
        var ran = false;
        registry.Register("S", "F", registered, _ =>
        {
            ran = true;
            return OverrideResult.Handled(PapyrusValue.None);
        });

        var result = registry.TryInvoke(Instance("S", "F", declared), "F", new PapyrusValue[received]);

        Assert.False(ran);
        Assert.Equal(OverrideResult.Refused(reason), result);
        Assert.Equal(1, registry.RefusedCount);
    }

    [Fact]
    public void AnyParameterCountAcceptsTheDeclaredCountAndAnUnknownDeclaredCountAcceptsTheRegisteredOne()
    {
        var registry = Registry();
        registry.Register("S", "F", ScriptOverrideRegistry.AnyParameterCount, _ => OverrideResult.Handled(PapyrusValue.None));
        registry.Register("S", "G", 2, _ => OverrideResult.Handled(PapyrusValue.None));

        Assert.Equal(OverrideOutcome.Handled, registry.TryInvoke(Instance("S", "F", 3), "F", new PapyrusValue[3]).Outcome);
        Assert.Equal(OverrideOutcome.Handled, registry.TryInvoke(Instance("S", "G", null), "G", new PapyrusValue[2]).Outcome);
        Assert.Equal(2, registry.HandledCount);
    }
}
