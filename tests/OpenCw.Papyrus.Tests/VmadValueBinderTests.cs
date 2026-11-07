using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vmad;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class VmadValueBinderTests
{
    [Fact]
    public void ReviewObjectBindingRetainsAnEngineReferenceInsteadOfAString()
    {
        var report = VmadValueBinder.Bind(Owner, new[]
        {
            new VmadValueShape("Target", "Object", Object: "000800:Quests.esp"),
        });
        var property = Assert.Single(report.Properties);
        Assert.Equal(PapyrusValueKind.Object, property.Value.Kind);
        Assert.Equal("000800:Quests.esp", Assert.IsType<EngineObjectReference>(property.Value.Reference).Identity);
        Assert.Null(property.Value.Text);
        Assert.Equal(1, report.Bound);
    }

    private static readonly PapyrusOwnerId Owner = PapyrusOwnerId.ForPlacedReference("000800:Quests.esp", "000A00");
    private static readonly PapyrusOwnerId Other = PapyrusOwnerId.ForPlacedReference("000800:Quests.esp", "000A01");

    [Fact]
    public void ScalarShapesBind()
    {
        var report = VmadValueBinder.Bind(Owner, new[]
        {
            new VmadValueShape("Count", "Int", Integer: 7),
            new VmadValueShape("Scale", "Float", Real: 1.5),
            new VmadValueShape("Enabled", "Bool", Boolean: true),
            new VmadValueShape("Label", "String", Text: "hello"),
        });

        Assert.True(report.Clean);
        Assert.Equal(4, report.Bound);
        Assert.Equal(0, report.Unsupported);
        Assert.Equal(7, report.Properties.Single(property => property.Name == "Count").Value.Integer);
        Assert.Equal("hello", report.Properties.Single(property => property.Name == "Label").Value.Text);
    }

    [Fact]
    public void AnObjectWithoutAnAliasBinds()
    {
        var report = VmadValueBinder.Bind(Owner, new[]
        {
            new VmadValueShape("Target", "Object", Object: "000800:Quests.esp"),
        });

        Assert.Equal(BoundKind.Object, Assert.Single(report.Properties).Kind);
        Assert.Equal(1, report.Bound);
    }

    [Fact]
    public void AnAliasBoundObjectIsExplicitAndNotDefaulted()
    {
        var report = VmadValueBinder.Bind(Owner, new[]
        {
            new VmadValueShape("Target", "Object", HasAlias: true, Object: "000800:Quests.esp"),
        });

        var property = Assert.Single(report.Properties);
        Assert.Equal(BoundKind.Unsupported, property.Kind);
        Assert.False(property.IsBound);
        Assert.Contains("alias", property.Reason!, StringComparison.Ordinal);
        Assert.Equal(0, report.SilentlyDefaulted);
        Assert.Single(report.UnsupportedShapes);
    }

    [Fact]
    public void ArrayAndStructShapesAreExplicitWithTheirElementCount()
    {
        var report = VmadValueBinder.Bind(Owner, new[]
        {
            new VmadValueShape("Items", "Array", ElementCount: 3),
            new VmadValueShape("Pair", "Struct", ElementCount: 2),
        });

        Assert.Equal(2, report.Unsupported);
        Assert.Contains("3 elements", string.Join(" ", report.UnsupportedShapes), StringComparison.Ordinal);
        Assert.Contains("2 members", string.Join(" ", report.UnsupportedShapes), StringComparison.Ordinal);
        Assert.All(report.Properties, property => Assert.Equal(BoundKind.Unsupported, property.Kind));
    }

    [Fact]
    public void AVariableShapeIsExplicit()
    {
        var report = VmadValueBinder.Bind(Owner, new[] { new VmadValueShape("Anything", "Variable") });

        Assert.Contains("Variable", Assert.Single(report.Properties).Reason!, StringComparison.Ordinal);
        Assert.Equal(1, report.Unsupported);
    }

    [Fact]
    public void EveryPropertyStaysOwnerLocal()
    {
        var first = VmadValueBinder.Bind(Owner, new[] { new VmadValueShape("Count", "Int", Integer: 1) });
        var second = VmadValueBinder.Bind(Other, new[] { new VmadValueShape("Count", "Int", Integer: 2) });

        Assert.Equal(Owner, Assert.Single(first.Properties).Owner);
        Assert.Equal(Other, Assert.Single(second.Properties).Owner);
        Assert.Equal(1, first.Properties[0].Value.Integer);
        Assert.Equal(2, second.Properties[0].Value.Integer);
    }

    [Fact]
    public void NoSupportedValueIsSilentlyDefaultedAndTheReportIsDeterministic()
    {
        var shapes = new List<VmadValueShape>
        {
            new("Zeta", "Array", ElementCount: 1),
            new("Alpha", "Int", Integer: 4),
            new("Beta", "Object", HasAlias: true),
            new("Gamma", "Unknown"),
        };

        var report = VmadValueBinder.Bind(Owner, shapes);
        var shuffled = VmadValueBinder.Bind(Owner, shapes.AsEnumerable().Reverse());

        Assert.True(report.Clean);
        Assert.Equal(0, report.SilentlyDefaulted);
        Assert.Equal(1, report.Bound);
        Assert.Equal(3, report.Unsupported);
        Assert.Equal(report.Properties.Count, report.Bound + report.Unsupported);
        Assert.Equal(report.ToReport(), shuffled.ToReport());
        Assert.Equal(report.Digest(), shuffled.Digest());
    }

    [Fact]
    public void TheAdapterReportsWhatRadicleBindsForTheSameShapes()
    {
        var ours = VmadValueBinder.Bind(Owner, new[]
        {
            new VmadValueShape("Count", "Int", Integer: 7),
            new VmadValueShape("Scale", "Float", Real: 1.5),
            new VmadValueShape("Enabled", "Bool", Boolean: true),
            new VmadValueShape("Label", "String", Text: "hello"),
            new VmadValueShape("Target", "Object", Object: "000800:Quests.esp"),
            new VmadValueShape("Bound", "Object", HasAlias: true, Object: "000800:Quests.esp"),
            new VmadValueShape("Items", "Array", ElementCount: 3),
            new VmadValueShape("Anything", "Variable"),
        });
        var radicle = Radicle.Interop.VmadBinder.Bind(
            Radicle.Interop.VmadOwner.ForPlacedReference("000800:Quests.esp", "000A00"),
            new[]
            {
                new Radicle.Interop.VmadShape("Count", "Int", Integer: 7),
                new Radicle.Interop.VmadShape("Scale", "Float", Real: 1.5),
                new Radicle.Interop.VmadShape("Enabled", "Bool", Boolean: true),
                new Radicle.Interop.VmadShape("Label", "String", Text: "hello"),
                new Radicle.Interop.VmadShape("Target", "Object", Object: "000800:Quests.esp"),
                new Radicle.Interop.VmadShape("Bound", "Object", HasAlias: true, Object: "000800:Quests.esp"),
                new Radicle.Interop.VmadShape("Items", "Array", ElementCount: 3),
                new Radicle.Interop.VmadShape("Anything", "Variable"),
            });

        Assert.Equal(radicle.Bound, ours.Bound);
        Assert.Equal(radicle.Unsupported, ours.Unsupported);
        Assert.Equal(radicle.UnsupportedShapes, ours.UnsupportedShapes);
        Assert.Equal(radicle.Properties.Select(property => (property.Name, property.Kind.ToString(), property.Reason)),
            ours.Properties.Select(property => (property.Name, property.Kind.ToString(), property.Reason)));
        Assert.Equal(1.5f, (float)ours.Properties.Single(property => property.Name == "Scale").Value.Real);
        Assert.True(ours.Properties.Single(property => property.Name == "Enabled").Value.Flag);
        Assert.Equal("hello", ours.Properties.Single(property => property.Name == "Label").Value.Text);
        Assert.All(radicle.Properties, property => Assert.Equal(Owner.Key, property.OwnerKey));
        Assert.All(ours.Properties, property => Assert.Equal(Owner, property.Owner));
    }

    [Fact]
    public void ValuesOutsideTheSingleWidthPapyrusTypesAreRefused()
    {
        var report = VmadValueBinder.Bind(Owner, new[]
        {
            new VmadValueShape("Big", "Int", Integer: 2147483648L),
            new VmadValueShape("Fine", "Int", Integer: 2147483647L),
            new VmadValueShape("Tenth", "Float", Real: 0.1d),
            new VmadValueShape("Half", "Float", Real: 1.5d),
        });
        Assert.Equal(2, report.Bound);
        Assert.Equal(2, report.Unsupported);
        Assert.Equal(new[] { "Big", "Tenth" },
            report.Properties.Where(property => !property.IsBound).Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(2147483647L, report.Properties.Single(property => property.Name == "Fine").Value.Integer);
    }

    [Fact]
    public void AShapeWithAnEmbeddedNulIsUnsupportedAndTheRestStillBind()
    {
        var report = VmadValueBinder.Bind(Owner, new[]
        {
            new VmadValueShape("Good", "Int", Integer: 3),
            new VmadValueShape("Bad", "String", Text: "a\0text"),
            new VmadValueShape("Also\0Bad", "Int", Integer: 1),
        });
        Assert.Equal(1, report.Bound);
        Assert.Equal(2, report.Unsupported);
        Assert.Equal(3, report.Properties.Count);
        Assert.False(report.Properties.Single(property => property.Name == "Bad").IsBound);
        Assert.Equal(3, report.Properties.Single(property => property.Name == "Good").Value.Integer);
        Assert.Contains($"{Owner.Key} Bad: the shape carries an embedded NUL", report.UnsupportedShapes);
        Assert.Contains($"{Owner.Key} Also\0Bad: the shape carries an embedded NUL", report.UnsupportedShapes);
    }

    [Fact]
    public void EveryOwnerKindCrossesToRadicleWithTheSameKey()
    {
        var owners = new[]
        {
            PapyrusOwnerId.ForForm("000800:Quests.esp"),
            PapyrusOwnerId.ForPlacedReference("000800:Quests.esp", "000A00"),
            PapyrusOwnerId.ForDynamicReference("000800:Quests.esp", "ff000001"),
            PapyrusOwnerId.ForQuestAlias("000800:Quests.esp", 4),
            PapyrusOwnerId.ForRuntimeInstance("000800:Quests.esp", 9),
        };
        Assert.Equal(Enum.GetValues<PapyrusOwnerKind>().Length, owners.Select(owner => owner.Kind).Distinct().Count());
        foreach (var owner in owners)
        {
            var radicle = Radicle.Interop.VmadBinder.Bind(
                new Radicle.Interop.VmadOwner((Radicle.Interop.Native.PapyrusOwnerKind)owner.Kind, owner.BaseFormKey, owner.Discriminator),
                new[] { new Radicle.Interop.VmadShape("Count", "Int", Integer: 1) });
            Assert.Equal(owner.Key, Assert.Single(radicle.Properties).OwnerKey);
            Assert.Equal(owner, Assert.Single(VmadValueBinder.Bind(owner,
                new[] { new VmadValueShape("Count", "Int", Integer: 1) }).Properties).Owner);
        }
    }
}
