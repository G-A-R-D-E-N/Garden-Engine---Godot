namespace OpenCommonwealth.Services.Ui.Service;

public readonly record struct ServiceResult(bool Ok, string Message)
{
    public static ServiceResult Success(string message = "") => new(true, message);

    public static ServiceResult Fail(string message) => new(false, message);
}
