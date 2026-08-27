using Core = Shackles.JobObjects;

namespace Shackles.App.Infrastructure;

internal static class JobOpenErrorFormatter
{
    public static string Format(string jobName, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var rawError = ToUserMessage(exception);
        if (exception is Core.JobObjectException
            {
                Operation: Core.JobOperation.OpenJob,
                NativeErrorCode: 2
            })
        {
            return $"{rawError}{Environment.NewLine}{Environment.NewLine}" +
                   $"Windows could not find '{jobName}' as an exact, case-sensitive name. " +
                   @"Unprefixed and Local\ names must be opened from the same Windows session. " +
                   "If the last job handle closed, Windows removes the name even though member processes can remain constrained.";
        }

        if (exception is Core.JobObjectException
            {
                Operation: Core.JobOperation.OpenJob,
                NativeErrorCode: 5
            })
        {
            return $"{rawError}{Environment.NewLine}{Environment.NewLine}" +
                   "Windows denied the access Shackles needs to view, configure, and assign processes to this Job Object.";
        }

        return $"{rawError}{Environment.NewLine}{Environment.NewLine}" +
               $"Shackles could not open the named Job Object '{jobName}'.";
    }

    private static string ToUserMessage(Exception exception)
    {
        var message = exception.Message.Trim();
        return string.IsNullOrWhiteSpace(message) ? "Windows rejected the operation." : message;
    }
}
