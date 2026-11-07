using System;
using System.Linq;
using System.Reflection;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class IrBoundaryTests
{
    [Fact]
    public void TheVmLayerCannotSeeRadicleOrPex()
    {
        var assembly = typeof(PapyrusVm).Assembly;
        var vmNamespace = typeof(PapyrusVm).Namespace!;

        var offenders = assembly.GetTypes()
            .Where(type => type.Namespace == vmNamespace)
            .SelectMany(ExposedTypes)
            .Where(exposed => exposed.Assembly.GetName().Name!.StartsWith("Radicle", StringComparison.OrdinalIgnoreCase)
                || exposed.Name.Contains("Pex", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
        Assert.DoesNotContain(assembly.GetTypes(), type => type.Name.Contains("Pex", StringComparison.Ordinal));
    }

    // Requires: the Vm layer exposes only engine types, so the Radicle seam stays in Host.
    private static IEnumerable<Type> ExposedTypes(Type type)
    {
        var seen = new HashSet<Type>();
        var frontier = new Stack<Type>();
        if (type.BaseType is not null) frontier.Push(type.BaseType);
        foreach (var face in type.GetInterfaces()) frontier.Push(face);
        foreach (var attribute in type.GetCustomAttributesData())
        {
            frontier.Push(attribute.AttributeType);
            foreach (var argument in attribute.ConstructorArguments) frontier.Push(argument.ArgumentType);
            foreach (var named in attribute.NamedArguments) frontier.Push(named.TypedValue.ArgumentType);
        }
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            switch (member)
            {
                case FieldInfo field:
                    frontier.Push(field.FieldType);
                    break;
                case PropertyInfo property:
                    frontier.Push(property.PropertyType);
                    break;
                case MethodBase method:
                    if (method is MethodInfo returns) frontier.Push(returns.ReturnType);
                    foreach (var parameter in method.GetParameters()) frontier.Push(parameter.ParameterType);
                    break;
                case EventInfo @event when @event.EventHandlerType is not null:
                    frontier.Push(@event.EventHandlerType);
                    break;
            }
        }
        while (frontier.Count > 0)
        {
            var current = frontier.Pop();
            if (!seen.Add(current)) continue;
            yield return current;
            if (current.HasElementType && current.GetElementType() is { } element) frontier.Push(element);
            foreach (var argument in current.GetGenericArguments()) frontier.Push(argument);
        }
    }

    [Fact]
    public void AnIrInstructionHasNamedFieldsAndNoOperandPositions()
    {
        var members = typeof(PapyrusInstruction).GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(member => member.Name).ToList();

        Assert.DoesNotContain(members, name => name.Contains("Operand", StringComparison.Ordinal));
        Assert.Contains(nameof(PapyrusInstruction.Receiver), members);
        Assert.Contains(nameof(PapyrusInstruction.Arguments), members);
        Assert.DoesNotContain(typeof(PapyrusInstruction).GetProperties(),
            property => property.PropertyType == typeof(PapyrusValue[])
                        || property.GetIndexParameters().Length > 0);
    }

    [Fact]
    public void TwoScriptsCanOwnTheSameNativeFunctionName()
    {
        Assert.NotEqual(new NativeId("Debug", "Trace"), new NativeId("Utility", "Trace"));
        Assert.Equal(new NativeId("debug", "TRACE"), NativeSet.DebugTrace);
        Assert.Equal("Debug.Trace", NativeSet.DebugTrace.ToString());
    }
}
