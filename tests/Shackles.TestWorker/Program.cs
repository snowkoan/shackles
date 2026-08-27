using System.Globalization;
using Shackles.JobObjects;

namespace Shackles.TestWorker;

internal static class Program
{
    private const string CreateNamedJobOwnerMode = "--create-named-job-owner";
    private static long _counter;

    private static int Main(string[] arguments)
    {
        if (arguments is [CreateNamedJobOwnerMode, var jobName])
        {
            return RunNamedJobOwner(jobName);
        }

        if (arguments.Length != 0)
        {
            Console.Error.WriteLine("Unsupported test-worker arguments.");
            return 2;
        }

        while (true)
        {
            Interlocked.Increment(ref _counter);
        }
    }

    private static int RunNamedJobOwner(string jobName)
    {
        var executable = Environment.ProcessPath ??
            throw new InvalidOperationException("The test worker executable path is unavailable.");
        using var job = JobObject.Create(jobName);
        job.SetExtendedLimits(new JobExtendedLimits { PriorityClass = JobPriorityClass.Idle });
        var member = job.LaunchProcess(new ProcessLaunchOptions(executable)
        {
            CreateNoWindow = true
        });
        var createdNew = job.CreatedNew;

        job.Dispose();
        var detachError = job.LastNotificationDetachError;
        Console.WriteLine(string.Join(
            '|',
            member.ProcessId.ToString(CultureInfo.InvariantCulture),
            createdNew ? "1" : "0",
            (detachError?.NativeErrorCode ?? 0).ToString(CultureInfo.InvariantCulture)));
        Console.Out.Flush();
        return detachError is null ? 0 : 3;
    }
}
