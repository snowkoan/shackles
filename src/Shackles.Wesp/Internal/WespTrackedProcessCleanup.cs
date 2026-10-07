namespace Shackles.Wesp.Internal;

internal static class WespTrackedProcessCleanup
{
    internal static void RemoveConfirmedExits(IList<TrackedWespProcess> processes)
    {
        for (var index = processes.Count - 1; index >= 0; index--)
        {
            var info = processes[index].GetInfo();
            if (!info.IsRunning && !info.IsStateUnknown)
            {
                processes[index].Dispose();
                processes.RemoveAt(index);
            }
        }
    }

    internal static WespException? Close(IList<TrackedWespProcess> processes,
        uint waitMilliseconds = 2_000)
    {
        var errors = new Dictionary<TrackedWespProcess, List<string>>();
        var launched = processes.Where(process => process.Origin == WespProcessOrigin.Launched).ToArray();
        foreach (var process in launched)
        {
            Capture(process, process.RequestTermination());
        }

        var deadline = Environment.TickCount64 + waitMilliseconds;
        foreach (var process in launched)
        {
            Capture(process, process.WaitForExit(checked((uint)Math.Max(0, deadline - Environment.TickCount64))));
        }

        for (var index = processes.Count - 1; index >= 0; index--)
        {
            var process = processes[index];
            var info = process.GetInfo();
            if (process.Origin == WespProcessOrigin.Attached || (!info.IsRunning && !info.IsStateUnknown))
            {
                process.Dispose();
                processes.RemoveAt(index);
            }
            else if (info.StateError is { } error)
            {
                CaptureMessage(process, error);
            }
        }

        if (processes.Count == 0)
        {
            return null;
        }

        var details = processes.Select(process => errors.TryGetValue(process, out var failures)
            ? $"PID {process.ProcessId}: {string.Join(" ", failures.Distinct())}"
            : $"PID {process.ProcessId} has not exited.");
        return new WespException(WespOperation.CloseSession,
            $"Termination could not be confirmed. {string.Join(" ", details)} " +
            "The process handles and WESP rules remain active; close those processes and retry closing the session.");

        void Capture(TrackedWespProcess process, WespException? error)
        {
            if (error is null)
            {
                return;
            }

            CaptureMessage(process, error.Message);
        }

        void CaptureMessage(TrackedWespProcess process, string message)
        {
            if (!errors.TryGetValue(process, out var failures))
            {
                failures = [];
                errors.Add(process, failures);
            }

            failures.Add(message);
        }
    }
}
