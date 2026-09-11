using Shackles.Wesp.Internal;

namespace Shackles.Wesp;

public static class WespStartupTroubleshooting
{
    public static string FormatStartupFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (IsIntegrityFailure(exception))
        {
            return FormatIntegrityFailure(exception);
        }

        if (!IsClientSessionFailure(exception))
        {
            return exception.Message;
        }

        return FormatStartupFailure(exception, WespDriverStatusProbe.GetStatus());
    }

    internal static string FormatStartupFailure(
        Exception exception,
        WespDriverServiceStatus driverStatus)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (IsIntegrityFailure(exception))
        {
            return FormatIntegrityFailure(exception);
        }

        if (!IsClientSessionFailure(exception))
        {
            return exception.Message;
        }

        var driverCheck = driverStatus switch
        {
            WespDriverServiceStatus.Running =>
                "The expected 'wesp' driver service currently reports that it is running.",
            WespDriverServiceStatus.NotRunning =>
                "The expected 'wesp' driver service is installed but does not currently report that it is running.",
            WespDriverServiceStatus.NotInstalled =>
                "The 'wesp' driver service was not found.",
            _ =>
                "Shackles could not verify the 'wesp' driver service state."
        };

        return string.Join(
            Environment.NewLine,
            "WESP Blocking could not establish a client session.",
            string.Empty,
            "Things to check:",
            "- Ensure Windows test-signing is enabled for this boot.",
            "- Ensure the WESP driver is installed and running.",
            $"  {driverCheck}",
            "- Ensure no other Shackles instance has an active WESP Blocking session.",
            string.Empty,
            "Technical details:",
            exception.Message);
    }

    private static string FormatIntegrityFailure(Exception exception) => string.Join(
        Environment.NewLine,
        "WESP Blocking requires Shackles to run with high integrity.",
        string.Empty,
        "Restart Shackles using Run as administrator, then start WESP Blocking again.",
        string.Empty,
        "Technical details:",
        exception.Message);

    private static bool IsIntegrityFailure(Exception exception) =>
        exception is WespException { Operation: WespOperation.CheckIntegrity };

    private static bool IsClientSessionFailure(Exception exception) =>
        exception is WespException
        {
            Operation: WespOperation.ClearPreviousClientState or
                WespOperation.RegisterClient or
                WespOperation.ConnectClient or
                WespOperation.ClearRules
        };
}
