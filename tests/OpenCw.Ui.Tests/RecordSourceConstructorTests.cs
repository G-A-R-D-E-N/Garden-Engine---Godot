using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class RecordSourceConstructorTests
{
    private sealed class CountingSource : IRecordSource
    {
        public int Reads { get; private set; }

        public RadicleEnvironment Environment
        {
            get
            {
                Reads++;
                return null!;
            }
        }

        public StringTables Strings
        {
            get
            {
                Reads++;
                return null!;
            }
        }
    }

    private static readonly IRecordSource NoSource = null!;
    private static readonly RadicleEnvironment NoEnvironment = null!;
    private static readonly StringTables NoStrings = null!;

    public static TheoryData<Func<object>> NullSource() => new()
    {
        () => new RadicleRecordReader(NoSource),
        () => new RadicleContainerRecords(NoSource, 1),
        () => new RadicleNpcInventoryRecords(NoSource, 1),
        () => new RadicleTerminalRecords(NoSource),
        () => new RadicleVendorRecords(NoSource),
        () => new RadicleVendorLookup(NoSource),
    };

    public static TheoryData<Func<object>> NullPair() => new()
    {
        () => new RadicleRecordReader(NoEnvironment, NoStrings),
        () => new RadicleContainerRecords(NoEnvironment, NoStrings, 1),
        () => new RadicleNpcInventoryRecords(NoEnvironment, NoStrings, 1),
        () => new RadicleTerminalRecords(NoEnvironment, NoStrings),
        () => new RadicleVendorRecords(NoEnvironment, NoStrings),
    };

    [Theory]
    [MemberData(nameof(NullSource))]
    public void A_null_record_source_is_rejected(Func<object> build)
    {
        Assert.Throws<ArgumentNullException>(build);
    }

    [Theory]
    [MemberData(nameof(NullPair))]
    public void The_environment_and_string_pair_still_rejects_nulls(Func<object> build)
    {
        Assert.Throws<ArgumentNullException>(build);
    }

    [Fact]
    public void Readers_built_from_a_source_do_not_read_it_until_used()
    {
        var source = new CountingSource();
        _ = new RadicleRecordReader(source);
        _ = new RadicleContainerRecords(source, 1);
        _ = new RadicleNpcInventoryRecords(source, 1);
        _ = new RadicleTerminalRecords(source);
        _ = new RadicleVendorRecords(source);
        _ = new RadicleVendorLookup(source);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public void A_reader_built_from_a_source_answers_an_unparsable_key_without_reading_the_environment()
    {
        var source = new CountingSource();
        var reader = new RadicleRecordReader(source);
        Assert.Null(reader.Signature("not a key"));
        Assert.Equal(0, source.Reads);
    }
}
