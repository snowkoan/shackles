using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Internal;

internal static class WespProcessLauncher
{
    private const uint ResumeFailed = uint.MaxValue;

    internal static TrackedWespProcess Launch(
        nint client,
        ulong policyId,
        string rootExecutablePath,
        WespLaunchOptions options,
        out WespLaunchResult result)
    {
        ArgumentNullException.ThrowIfNull(options);
        var executablePath = WespPolicyNormalizer.NormalizeRootExecutable(options.FileName);
        if (!string.Equals(executablePath, rootExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new WespException(
                WespOperation.ValidatePolicy,
                "An active WESP policy can launch only the executable it was created for. Close it before choosing another executable.");
        }

        var arguments = options.Arguments ??
            throw new WespException(WespOperation.ValidatePolicy, "Arguments cannot be null.");
        if (arguments.Contains('\0'))
        {
            throw new WespException(
                WespOperation.ValidatePolicy,
                "Arguments cannot contain a null character.");
        }

        var commandLineText = string.Concat(
            QuoteArgument(executablePath),
            string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments);
        if (commandLineText.Length >= 32767)
        {
            throw new WespException(
                WespOperation.ValidatePolicy,
                "The command line is too long for Windows.");
        }

        var commandLine = string.Concat(commandLineText, '\0').ToCharArray();
        var workingDirectory = WespPolicyNormalizer.NormalizeWorkingDirectory(
            options.WorkingDirectory,
            executablePath);
        var startupInfo = new NativeStartupInfo
        {
            Size = checked((uint)Marshal.SizeOf<NativeStartupInfo>())
        };

        NativeProcessInformation processInformation;
        unsafe
        {
            fixed (char* commandLineBuffer = commandLine)
            {
                if (!NativeMethods.CreateProcess(
                        executablePath,
                        commandLineBuffer,
                        0,
                        0,
                        inheritHandles: false,
                        ProcessCreationFlags.Suspended,
                        0,
                        workingDirectory,
                        ref startupInfo,
                        out processInformation))
                {
                    throw FromWin32(
                        WespOperation.CreateProcess,
                        "Windows could not create the process in a suspended state.");
                }
            }
        }

        SafeProcessHandle? process = new(processInformation.Process);
        using var thread = new SafeThreadHandle(processInformation.Thread);
        var resumed = false;
        try
        {
            if (!NativeMethods.GetProcessTimes(
                    process,
                    out var creationTime,
                    out _,
                    out _,
                    out _))
            {
                throw FromWin32(
                    WespOperation.CreateProcess,
                    "Windows could not capture the process identity.");
            }

            var creationTimeValue = creationTime.ToLong();
            WespProcessTagger.TagProcess(
                client,
                checked((uint)processInformation.ProcessId),
                policyId,
                "the suspended process");

            if (NativeMethods.ResumeThread(thread) == ResumeFailed)
            {
                throw FromWin32(
                    WespOperation.ResumeProcess,
                    "Windows could not resume the policy-bound process.");
            }

            resumed = true;
            var processId = checked((int)processInformation.ProcessId);
            result = new WespLaunchResult(
                processId,
                creationTimeValue,
                [
                    "WESP client-session rules remain active only while this policy is open in Shackles.",
                    "This proof of concept covers direct operations from the tagged process tree; inherited handles, path aliases, and brokered work remain outside its claim."
                ]);
            var tracked = new TrackedWespProcess(
                process,
                processId,
                creationTimeValue,
                WespProcessOrigin.Launched);
            process = null;
            return tracked;
        }
        finally
        {
            if (!resumed && process is { IsClosed: false, IsInvalid: false })
            {
                _ = NativeMethods.TerminateProcess(process, 1);
            }

            process?.Dispose();
        }
    }

    internal static string QuoteArgument(string value)
    {
        var result = new StringBuilder(value.Length + 2);
        result.Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', checked((backslashes * 2) + 1));
                result.Append('"');
                backslashes = 0;
                continue;
            }

            result.Append('\\', backslashes);
            result.Append(character);
            backslashes = 0;
        }

        result.Append('\\', checked(backslashes * 2));
        result.Append('"');
        return result.ToString();
    }

    private static WespException FromWin32(WespOperation operation, string detail)
    {
        var error = Marshal.GetLastWin32Error();
        return new WespException(
            operation,
            $"{detail} {new Win32Exception(error).Message}",
            unchecked((int)(0x80070000u | (uint)error)));
    }

}
