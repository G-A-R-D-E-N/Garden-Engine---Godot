using System.Text;
using ConditionFunctions = OpenCommonwealth.ConditionFunctions;
using Radicle.Interop;
using ParamKind = Radicle.Interop.Native.ConditionParamKind;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ConditionParametersExportTests
{
    private const string PluginName = "ConditionParams.esp";

    private static readonly FormKey Target = new(PluginName, 0x801);

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        return stream.ToArray();
    }

    private static byte[] Ctda(byte flags, ushort function, int param1, uint comparison = 0, uint runOn = 0, uint reference = 0) =>
        Bytes(w =>
        {
            w.Write(flags); w.Write(new byte[3]); w.Write(comparison); w.Write(function); w.Write((ushort)0);
            w.Write(param1); w.Write(0); w.Write(runOn); w.Write(reference); w.Write(-1);
        });

    private static IReadOnlyList<RadicleTypedCondition> Typed(params byte[][] conditions)
    {
        using var file = PluginFile.Create(PluginName);
        file.SetNextFormId(0x800);
        var perk = file.CreateRawRecord("PERK", 131);
        if (perk.LocalId != 0x800) throw new InvalidOperationException($"Radicle allocated {perk.LocalId:X} instead of 800");
        file.SetSubrecordBytes(perk, "EDID", Encoding.UTF8.GetBytes("Typed\0"));
        for (var i = 0; i < conditions.Length; i++) file.SetSubrecordBytes(perk, "CTDA", conditions[i], i);
        var directory = Directory.CreateTempSubdirectory("garden-condition-params-");
        try
        {
            var path = Path.Combine(directory.FullName, PluginName);
            file.Write(path);
            using var environment = RadicleEnvironment.Load(new[] { path });
            var record = environment.Winner(new FormKey(PluginName, 0x800))!.Value;
            return environment.ConditionParameters(record, environment.Conditions(record));
        }
        finally { directory.Delete(true); }
    }

    // Constraint: Regression: a function's form-link first parameter must come back as a resolved key and a numeric parameter or an id outside the table must not, as the removed hand-parsing decided per function id.
    [Fact]
    public void A_form_link_first_parameter_carries_its_key_and_other_functions_carry_none()
    {
        ushort[] forms = [56, 58, 59, 69, 71, 72, 73, 74, 426, 543, 560];
        ushort[] plain = [18, 46, 80];
        var typed = Typed(forms.Concat(plain).Append((ushort)57).Select(function => Ctda(0, function, 0x801)).ToArray());

        for (var i = 0; i < forms.Length; i++)
            Assert.Equal(Target, ConditionFunctions.FormOf(typed[i].Param1));
        for (var i = forms.Length; i < forms.Length + plain.Length; i++)
            Assert.Null(ConditionFunctions.FormOf(typed[i].Param1));
        Assert.Null(typed[^1].FunctionName);
        Assert.Null(ConditionFunctions.FormOf(typed[^1].Param1));
    }

    // Constraint: Regression: an alias or package-data switched reference slot is an index, not a FormID, so FormOf must give no key for it while the plain slot still resolves.
    [Fact]
    public void An_alias_or_package_data_switched_slot_is_an_index_without_a_key()
    {
        var typed = Typed(Ctda(0x02, 639, 3), Ctda(0x08, 639, 3), Ctda(0, 639, 0x801));

        foreach (var switched in typed.Take(2))
        {
            Assert.Equal(ParamKind.Index, switched.Param1.Kind);
            Assert.Equal(3, switched.Param1.Raw);
            Assert.Null(ConditionFunctions.FormOf(switched.Param1));
        }
        Assert.Equal(Target, ConditionFunctions.FormOf(typed[2].Param1));
    }

    // Constraint: Regression: one FormID outside the record's masters must leave only its own slot unresolved and must not fail or blank the other conditions of the list.
    [Fact]
    public void One_unresolved_form_id_leaves_only_its_own_slot_without_a_key()
    {
        var typed = Typed(Ctda(0, 72, 0x01000800), Ctda(0, 72, 0x801));

        Assert.Equal(ParamKind.Form, typed[0].Param1.Kind);
        Assert.True(typed[0].Param1.FormUnresolved);
        Assert.Null(ConditionFunctions.FormOf(typed[0].Param1));
        Assert.Equal(Target, ConditionFunctions.FormOf(typed[1].Param1));
    }

    // Constraint: Regression: the comparison must be a global key only under the use-global flag and a float otherwise, and the reference must carry a key only for a run-on Reference condition.
    [Fact]
    public void The_comparison_global_and_the_reference_resolve_only_where_the_row_says_so()
    {
        var typed = Typed(
            Ctda(0x04, 80, 0, comparison: 0x802),
            Ctda(0, 80, 0, comparison: BitConverter.SingleToUInt32Bits(1f)),
            Ctda(0, 72, 0x801, runOn: 2, reference: 0x803),
            Ctda(0, 72, 0x801));

        Assert.Equal(new FormKey(PluginName, 0x802), ConditionFunctions.FormOf(typed[0].Comparison));
        Assert.Equal(ParamKind.Float, typed[1].Comparison.Kind);
        Assert.Equal(1.0, typed[1].Comparison.Number);
        Assert.Null(ConditionFunctions.FormOf(typed[1].Comparison));
        Assert.Equal(new FormKey(PluginName, 0x803), ConditionFunctions.FormOf(typed[2].Reference));
        Assert.Null(ConditionFunctions.FormOf(typed[3].Reference));
    }
}
