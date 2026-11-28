namespace OpenCommonwealth.Services;

public static class ConvertStageBatchExit
{
    public static int ExitCode(long failed, long succeeded) => failed > 0 && succeeded == 0 ? 1 : 0;
}