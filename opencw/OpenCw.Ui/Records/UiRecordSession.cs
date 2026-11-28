namespace OpenCommonwealth.Services.Ui.Records;

public static class UiRecordSession
{
    public static bool IsLiveEnvironment(object? recordEnvironment, object? loadedEnvironment) =>
        recordEnvironment is not null && ReferenceEquals(recordEnvironment, loadedEnvironment);

    public static bool IsLiveRecords(object? records, object? recordEnvironment, object? loadedEnvironment) =>
        records is not null && IsLiveEnvironment(recordEnvironment, loadedEnvironment);

    public static bool IsLiveRecords(bool hasLoadOrder, object? records, object? recordEnvironment, object? loadedEnvironment) =>
        hasLoadOrder && IsLiveRecords(records, recordEnvironment, loadedEnvironment);

    public static bool IsSameRecords(object? held, object? active) =>
        ReferenceEquals(held, active);

    public static bool IsCurrentSession(int built, int current) =>
        built >= 0 && built == current;
}
