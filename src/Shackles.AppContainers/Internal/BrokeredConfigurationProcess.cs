using System.Diagnostics;

namespace Shackles.AppContainers.Internal;

internal interface IBrokeredConfigurationProcess : IDisposable
{
    bool Start();
    void StartReading();
    bool WaitForExit(int milliseconds);
    bool WaitForOutput(int milliseconds);
    int ExitCode { get; }
    void Terminate();
    void CancelOutput();
}

internal static class BrokeredConfigurationProcessRunner
{
    internal static int Run(IBrokeredConfigurationProcess process,
        int timeoutMilliseconds = 10_000, int cleanupMilliseconds = 1_000,
        Func<long>? getMilliseconds = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(cleanupMilliseconds);
        var clock = getMilliseconds ?? (() => Environment.TickCount64);
        var deadline = clock() + timeoutMilliseconds;
        // Reserve part of the same deadline for termination after a stalled exit or output pipe.
        var completionDeadline = deadline - Math.Min(cleanupMilliseconds, timeoutMilliseconds);
        var started = false;
        try
        {
            started = process.Start();
            if (!started)
            {
                throw new InvalidOperationException("Windows did not start bfscfg.exe.");
            }

            process.StartReading();

            if (!process.WaitForExit(Remaining(completionDeadline)) ||
                !process.WaitForOutput(Remaining(completionDeadline)))
            {
                throw new TimeoutException(
                    "bfscfg.exe did not exit and finish capturing its output before the operation deadline. " +
                    "The BFS policy state is uncertain; close the sandbox to retry cleanup.");
            }

            return process.ExitCode;
        }
        catch (Exception exception)
        {
            if (!started)
            {
                throw;
            }

            var cleanup = new List<string>();
            try
            {
                process.Terminate();
            }
            catch (Exception terminationError)
            {
                cleanup.Add($"Helper termination failed: {terminationError.Message}");
            }

            try
            {
                if (!process.WaitForExit(Remaining(deadline)))
                {
                    cleanup.Add("Helper exit could not be confirmed within the remaining deadline.");
                }
            }
            catch (Exception waitError)
            {
                cleanup.Add($"Helper exit could not be confirmed: {waitError.Message}");
            }

            try
            {
                process.CancelOutput();
            }
            catch (Exception outputError)
            {
                cleanup.Add($"Output capture could not be cancelled: {outputError.Message}");
            }

            throw new InvalidOperationException(
                $"{exception.Message} {string.Join(" ", cleanup)}".Trim(), exception);
        }

        int Remaining(long target) => checked((int)Math.Clamp(target - clock(), 0, int.MaxValue));
    }
}

internal sealed class BrokeredConfigurationProcess : IBrokeredConfigurationProcess
{
    private readonly Process _process;
    private readonly TaskCompletionSource _stdoutEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stderrEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _stdoutStarted;
    private bool _stderrStarted;

    internal BrokeredConfigurationProcess(ProcessStartInfo startInfo, Action<string, string?> appendOutput)
    {
        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = false };
        _process.OutputDataReceived += (_, args) => Receive("stdout", args.Data, _stdoutEnded);
        _process.ErrorDataReceived += (_, args) => Receive("stderr", args.Data, _stderrEnded);

        void Receive(string stream, string? line, TaskCompletionSource ended)
        {
            if (line is null)
            {
                ended.TrySetResult();
            }
            else
            {
                appendOutput(stream, line);
            }
        }
    }

    public bool Start() => _process.Start();

    public void StartReading()
    {
        _process.BeginOutputReadLine();
        _stdoutStarted = true;
        _process.BeginErrorReadLine();
        _stderrStarted = true;
    }

    public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);
    public bool WaitForOutput(int milliseconds) =>
        Task.WhenAll(_stdoutEnded.Task, _stderrEnded.Task).Wait(milliseconds);
    public int ExitCode => _process.ExitCode;
    public void Terminate()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
    }

    public void CancelOutput()
    {
        Exception? failure = null;
        if (_stdoutStarted)
        {
            try
            {
                _process.CancelOutputRead();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                _stdoutStarted = false;
            }
        }

        if (_stderrStarted)
        {
            try
            {
                _process.CancelErrorRead();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
            finally
            {
                _stderrStarted = false;
            }
        }

        if (failure is not null)
        {
            throw new InvalidOperationException("Could not cancel helper output capture.", failure);
        }
    }

    public void Dispose()
    {
        try
        {
            CancelOutput();
        }
        catch (Exception)
        {
            // The runner reports cancellation failures before disposal. This fallback must not
            // replace a timeout or the helper's result while releasing the process/pipe handles.
        }
        finally
        {
            _process.Dispose();
        }
    }
}
