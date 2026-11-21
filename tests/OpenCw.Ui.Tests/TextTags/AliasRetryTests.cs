using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.TextTags;
using Radicle.Interop;
using Xunit;
using AliasKind = Radicle.Interop.Native.QuestAliasKind;
using FillType = Radicle.Interop.Native.QuestFillType;

namespace OpenCw.Ui.Tests;

public class AliasRetryTests
{
    const string QuestA = "000A00:A.esp";
    const string QuestB = "000B00:A.esp";
    static readonly FormKey Placed = new("A.esp", 0x800);

    static QuestStoredAlias Alias(uint id, string name) =>
        new(AliasKind.Reference, id, Encoding.UTF8.GetBytes(name), 0, null, null,
            new QuestStoredFill(true, FillType.SpecificReference, Placed, null, null, 0, null, 0, 0, 0), Array.Empty<QuestStoredCollectionEntry>());

    static IEnumerable<(string Quest, IReadOnlyList<QuestStoredAlias> Aliases)> Rows(bool failAfterFirst)
    {
        yield return (QuestA, new[] { Alias(1, "Console") });
        if (failAfterFirst) throw new InvalidOperationException("bulk read failed");
        yield return (QuestB, new[] { Alias(2, "Nick") });
    }

    // Regression (finding 519): a failed bulk read must not leave an empty or partial index behind for the rest of the session.
    [Fact]
    public void A_failed_bulk_read_is_not_kept_and_the_next_call_reads_the_whole_index()
    {
        var failing = true;
        var reads = 0;
        var names = new RadicleQuestAliasNames(() => { reads++; return Rows(failing); });

        Assert.Throws<InvalidOperationException>(() => names.Fills());
        Assert.Null(names.Aliases(QuestA));
        Assert.Equal(2, reads);

        failing = false;
        Assert.Equal(2, names.Fills().Count);
        Assert.Equal("Nick", names.Aliases(QuestB)!.Single().Name);
        Assert.Equal(3, reads);

        failing = true;
        Assert.Equal(2, names.Fills().Count);
        Assert.Equal(3, reads);
    }

    [Fact]
    public void The_retry_gate_stays_closed_for_a_growing_delay_after_each_failure_and_reopens_on_success()
    {
        var now = 100.0;
        var gate = new RetryGate(() => now, firstDelaySeconds: 5, maxDelaySeconds: 12);
        Assert.True(gate.Open);

        gate.Failed();
        now = 104.9;
        Assert.False(gate.Open);
        now = 105;
        Assert.True(gate.Open);

        gate.Failed();
        now = 114.9;
        Assert.False(gate.Open);
        now = 115;
        Assert.True(gate.Open);

        gate.Failed();
        now = 126.9;
        Assert.False(gate.Open);
        now = 127;
        Assert.True(gate.Open);

        gate.Succeeded();
        Assert.True(gate.Open);
        gate.Failed();
        now = 131.9;
        Assert.False(gate.Open);
        now = 132;
        Assert.True(gate.Open);
    }
}
