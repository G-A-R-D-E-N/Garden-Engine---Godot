using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MovementTypeSpeedsTests
{
    const string Id = "03580D:Fallout4.esm";

    static readonly string[] SchemaPaths =
    {
        @"MOVT\ForwardWalk", @"MOVT\ForwardRun", @"MOVT\ForwardSprint",
        @"MOVT\BackWalk", @"MOVT\BackRun", @"MOVT\BackSprint",
        @"MOVT\LeftWalk", @"MOVT\LeftRun", @"MOVT\LeftSprint",
        @"MOVT\RightWalk", @"MOVT\RightRun", @"MOVT\RightSprint",
    };

    static readonly string[] SchemaValues = { "80.1", "370", "500", "71.93", "260", "0", "80.09", "370", "0", "79.75", "370", "0" };

    static readonly MovementTypeSpeeds Fallback = new(
        new MovementRow(1f, 2f, 3f), new MovementRow(4f, 5f, 0f), new MovementRow(6f, 7f, 0f), new MovementRow(8f, 9f, 10f));

    static FakeRecordReader Reader(string editorId = "NPC_Default_MT")
    {
        var reader = new FakeRecordReader();
        var record = reader.Add(Id, "MOVT", editorId);
        for (var i = 0; i < SchemaPaths.Length; i++)
            record.Scalars[SchemaPaths[i]] = SchemaValues[i];
        return reader;
    }

    [Fact]
    public void The_twelve_paths_are_the_radicle_schema_paths_in_row_order()
    {
        Assert.Equal(SchemaPaths, MovementTypeSpeeds.All.ToArray());
    }

    [Fact]
    public void The_vanilla_slots_are_the_five_movement_type_records_with_the_vanilla_rows()
    {
        Assert.Equal(
            new[] { "NPC_Default_MT", "NPC_Sneaking", "NPC_Swimming_MT", "NPC_Sighted_MT", "PowerArmor_Player_Default_MT" },
            VanillaMovementSlots.All.Select(slot => slot.EditorId));
        Assert.Equal(new MovementTypeSpeeds(
            new MovementRow(80.1f, 370f, 500f), new MovementRow(71.93f, 260f, 0f), new MovementRow(80.09f, 370f, 0f), new MovementRow(79.75f, 370f, 0f)),
            VanillaMovementSlots.Default.Units);
        Assert.Equal(new MovementTypeSpeeds(
            new MovementRow(90f, 205f, 388.5f), new MovementRow(90f, 205f, 0f), new MovementRow(90f, 205f, 0f), new MovementRow(90f, 205f, 0f)),
            VanillaMovementSlots.Sneak.Units);
        Assert.Equal(new MovementTypeSpeeds(
            new MovementRow(80.1f, 370f, 500f), new MovementRow(71.93f, 205.25f, 0f), new MovementRow(80.09f, 370f, 0f), new MovementRow(79.75f, 370f, 0f)),
            VanillaMovementSlots.Swim.Units);
        Assert.Equal(new MovementTypeSpeeds(
            new MovementRow(100.76f, 200f, 350f), new MovementRow(100.76f, 200f, 0f), new MovementRow(100.76f, 200f, 0f), new MovementRow(100.76f, 200f, 0f)),
            VanillaMovementSlots.Sighted.Units);
        Assert.Equal(new MovementTypeSpeeds(
            new MovementRow(112.48f, 311.66f, 497.06f), new MovementRow(112.48f, 311.66f, 0f), new MovementRow(112.48f, 311.66f, 0f), new MovementRow(112.48f, 311.66f, 0f)),
            VanillaMovementSlots.PowerArmor.Units);
    }

    [Fact]
    public void Every_field_comes_from_the_record_and_a_zero_sprint_is_kept()
    {
        var log = new List<string>();
        var read = MovementTypeSpeeds.Read(Reader(), "NPC_Default_MT", Fallback, log.Add);
        Assert.Equal(new MovementRow(80.1f, 370f, 500f), read.Forward);
        Assert.Equal(new MovementRow(71.93f, 260f, 0f), read.Back);
        Assert.Equal(new MovementRow(80.09f, 370f, 0f), read.Left);
        Assert.Equal(new MovementRow(79.75f, 370f, 0f), read.Right);
        Assert.Empty(log);
    }

    [Fact]
    public void An_absent_or_unreadable_field_falls_back_alone_and_is_logged_once()
    {
        var reader = Reader();
        var record = reader.Records[Id];
        record.Scalars.Remove(@"MOVT\LeftRun");
        record.Scalars[@"MOVT\BackWalk"] = "not a number";
        record.Scalars[@"MOVT\RightSprint"] = "NaN";
        record.Scalars[@"MOVT\ForwardWalk"] = "-80.1";
        var log = new List<string>();
        var read = MovementTypeSpeeds.Read(reader, "NPC_Default_MT", Fallback, log.Add);
        Assert.Equal(new MovementRow(4f, 260f, 0f), read.Back);
        Assert.Equal(new MovementRow(80.09f, 7f, 0f), read.Left);
        Assert.Equal(new MovementRow(79.75f, 370f, 10f), read.Right);
        Assert.Equal(new MovementRow(1f, 370f, 500f), read.Forward);
        var line = Assert.Single(log);
        Assert.Contains("4 of 12", line);
    }

    [Fact]
    public void A_missing_record_returns_the_fallback_and_is_logged_once()
    {
        var log = new List<string>();
        var read = MovementTypeSpeeds.Read(Reader("Other_MT"), "NPC_Default_MT", Fallback, log.Add);
        Assert.Equal(Fallback, read);
        Assert.Single(log);
    }

    [Fact]
    public void A_record_of_another_signature_is_not_read()
    {
        var reader = new FakeRecordReader();
        reader.Add(Id, "GMST", "NPC_Default_MT");
        Assert.Equal(Fallback, MovementTypeSpeeds.Read(reader, "NPC_Default_MT", Fallback));
    }

    [Fact]
    public void A_record_with_no_speed_data_falls_back_for_all_twelve_values()
    {
        var reader = new FakeRecordReader();
        reader.Add(Id, "MOVT", "NPC_Default_MT");
        var log = new List<string>();
        Assert.Equal(Fallback, MovementTypeSpeeds.Read(reader, "NPC_Default_MT", Fallback, log.Add));
        Assert.Contains("12 of 12", Assert.Single(log));
    }

    [Fact]
    public void Read_all_reads_five_slots_and_leaves_each_slot_partial_fallback_alone()
    {
        var reader = Reader();
        foreach (var (formId, editorId) in new (string FormId, string EditorId)[]
        {
            ("03580F:Fallout4.esm", "NPC_Swimming_MT"),
            ("035810:Fallout4.esm", "NPC_Sighted_MT"),
            ("035811:Fallout4.esm", "PowerArmor_Player_Default_MT"),
        })
        {
            var record = reader.Add(formId, "MOVT", editorId);
            for (var i = 0; i < SchemaPaths.Length; i++)
                record.Scalars[SchemaPaths[i]] = SchemaValues[i];
        }
        var sneaking = reader.Add("03580E:Fallout4.esm", "MOVT", "NPC_Sneaking");
        for (var i = 0; i < SchemaPaths.Length; i++)
            sneaking.Scalars[SchemaPaths[i]] = SchemaValues[i];
        sneaking.Scalars.Remove(@"MOVT\ForwardWalk");
        var log = new List<string>();
        var units = VanillaMovementSlots.ReadAll(reader, log.Add);
        Assert.Equal(new MovementRow(80.1f, 370f, 500f), units.Default.Forward);
        Assert.Equal(90f, units.Sneak.Forward.Walk);
        Assert.Equal(370f, units.Sneak.Forward.Run);
        Assert.Contains("1 of 12", Assert.Single(log));
    }

    [Fact]
    public void Read_all_uses_builtin_rows_when_a_slot_record_is_missing()
    {
        var reader = Reader("NPC_Sneaking");
        var log = new List<string>();
        var units = VanillaMovementSlots.ReadAll(reader, log.Add);
        Assert.Equal(VanillaMovementSlots.Default.Units, units.Default);
        Assert.Equal(VanillaMovementSlots.Swim.Units, units.Swim);
        Assert.Equal(VanillaMovementSlots.Sighted.Units, units.Sighted);
        Assert.Equal(VanillaMovementSlots.PowerArmor.Units, units.PowerArmor);
        Assert.Equal(4, log.Count);
    }
}
