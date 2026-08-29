using System.Runtime.InteropServices;
using System.Text;
using Shackles.ExperimentalSandboxes.Interop;

namespace Shackles.ExperimentalSandboxes.Internal;

internal static class SandboxLauncher
{
    private const uint TokenQuery = 0x0008;
    private const int ErrorNotSupported = 50;

    internal static unsafe TrackedSandboxProcess Launch(
        SandboxIdentity identity,
        ExperimentalSandboxOptions sandboxOptions,
        ExperimentalSandboxLaunchOptions launchOptions,
        IReadOnlyList<string> initialWarnings)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(sandboxOptions);
        ArgumentNullException.ThrowIfNull(launchOptions);
        var executablePath = ValidateExecutable(launchOptions.FileName);
        var workingDirectory = ResolveWorkingDirectory(
            launchOptions.WorkingDirectory,
            executablePath,
            sandboxOptions.FileSystemRules);
        var arguments = launchOptions.Arguments ??
            throw new ArgumentException(
                "Arguments cannot be null.",
                nameof(launchOptions));
        if (arguments.Contains('\0'))
        {
            throw new ArgumentException(
                "Arguments cannot contain a null character.",
                nameof(launchOptions));
        }

        var support = SandboxSupportProbe.Probe();
        if (!support.IsAvailable)
        {
            throw new ExperimentalSandboxException(
                ExperimentalSandboxOperation.CheckSupport,
                support.Summary,
                support.ProbeErrorCode);
        }

        var specification = SandboxSpecificationSerializer.Serialize(sandboxOptions);
        if (!SandboxNativeApi.TryLoad(
                out var loaded,
                out var failure,
                out _,
                out _))
        {
            throw new ExperimentalSandboxException(
                ExperimentalSandboxOperation.CheckSupport,
                failure ?? "Could not load the experimental sandbox API.");
        }

        using var api = loaded!;
        var warnings = initialWarnings.ToList();
        nint environment = 0;
        SafeProcessHandle? process = null;
        SafeThreadHandle? thread = null;
        try
        {
            var creationFlags = ProcessCreationFlags.None;
            if (sandboxOptions.UseMinimalEnvironment)
            {
                environment = CreateCleanEnvironment();
                creationFlags |= ProcessCreationFlags.UnicodeEnvironment;
            }

            var processInformation = InvokeCreate(
                api.Create,
                executablePath,
                arguments,
                workingDirectory,
                identity.ProfileName,
                specification,
                creationFlags,
                environment,
                out var error);
            if (processInformation.Process == 0 &&
                environment != 0 &&
                error == ErrorNotSupported)
            {
                warnings.Add(
                    "This Windows build rejected an explicit environment block; " +
                    "the launch was retried with the caller's inherited environment.");
                processInformation = InvokeCreate(
                    api.Create,
                    executablePath,
                    arguments,
                    workingDirectory,
                    identity.ProfileName,
                    specification,
                    ProcessCreationFlags.None,
                    0,
                    out error);
            }

            if (processInformation.Process == 0)
            {
                throw ExperimentalSandboxException.FromWin32(
                    ExperimentalSandboxOperation.CreateProcess,
                    error,
                    $"Experimental_CreateProcessInSandbox could not start " +
                    $"'{executablePath}'.");
            }

            process = new SafeProcessHandle(processInformation.Process);
            thread = new SafeThreadHandle(processInformation.Thread);
            var result = new ExperimentalSandboxLaunchResult(
                checked((int)processInformation.ProcessId),
                ReadCreationTime(process),
                warnings.ToArray());
            var tracked = new TrackedSandboxProcess(process, result);
            process = null;
            return tracked;
        }
        finally
        {
            thread?.Dispose();
            if (process is { IsInvalid: false, IsClosed: false })
            {
                _ = NativeMethods.TerminateProcess(process, 1);
            }

            process?.Dispose();
            if (environment != 0)
            {
                _ = NativeMethods.DestroyEnvironmentBlock(environment);
            }
        }
    }

    private static unsafe NativeProcessInformation InvokeCreate(
        CreateProcessInSandboxDelegate create,
        string executablePath,
        string arguments,
        string workingDirectory,
        string identity,
        byte[] specification,
        ProcessCreationFlags creationFlags,
        nint environment,
        out int error)
    {
        var commandLine = string.Concat(
            QuoteArgument(executablePath),
            string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments,
            '\0').ToCharArray();
        var startupInfo = new NativeStartupInfo
        {
            Size = checked((uint)sizeof(NativeStartupInfo))
        };
        var processInformation = new NativeProcessInformation();
        fixed (char* commandLinePointer = commandLine)
        fixed (char* workingDirectoryPointer = workingDirectory)
        fixed (char* identityPointer = identity)
        fixed (byte* specificationPointer = specification)
        {
            Marshal.SetLastPInvokeError(0);
            var result = create(
                0,
                commandLinePointer,
                0,
                0,
                0,
                creationFlags,
                environment,
                (nint)workingDirectoryPointer,
                &startupInfo,
                (nint)identityPointer,
                specificationPointer,
                checked((uint)specification.Length),
                &processInformation);
            error = Marshal.GetLastPInvokeError();
            return result != 0 ? processInformation : default;
        }
    }

    private static nint CreateCleanEnvironment()
    {
        if (NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                TokenQuery,
                out var rawToken) == 0)
        {
            throw ExperimentalSandboxException.FromWin32(
                ExperimentalSandboxOperation.CreateProcess,
                Marshal.GetLastPInvokeError(),
                "Windows could not open the current user token for a clean environment.");
        }

        using var token = new SafeTokenHandle(rawToken);
        if (NativeMethods.CreateEnvironmentBlock(
                out var environment,
                token.DangerousGetHandle(),
                inherit: 0) == 0)
        {
            throw ExperimentalSandboxException.FromWin32(
                ExperimentalSandboxOperation.CreateProcess,
                Marshal.GetLastPInvokeError(),
                "Windows could not create a clean user environment block.");
        }

        return environment;
    }

    private static string ValidateExecutable(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('\0'))
        {
            throw new ArgumentException(
                "A valid executable path is required.",
                nameof(fileName));
        }

        var path = Path.GetFullPath(fileName);
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException("The executable was not found.", path);
    }

    private static string ResolveWorkingDirectory(
        string? requested,
        string executablePath,
        IReadOnlyList<ExperimentalSandboxFileRule> rules)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (requested.Contains('\0'))
            {
                throw new ArgumentException(
                    "The working directory cannot contain a null character.",
                    nameof(requested));
            }

            var fullPath = Path.GetFullPath(requested);
            return Directory.Exists(fullPath)
                ? fullPath
                : throw new DirectoryNotFoundException(
                    $"The working directory does not exist: {fullPath}");
        }

        var granted = rules.FirstOrDefault(rule =>
            rule.Access != ExperimentalSandboxFileAccess.Deny &&
            Directory.Exists(rule.Path));
        return granted?.Path ??
               Path.GetDirectoryName(executablePath) ??
               Path.GetPathRoot(executablePath) ??
               Environment.GetFolderPath(Environment.SpecialFolder.System);
    }

    private static string QuoteArgument(string value)
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

    private static long ReadCreationTime(SafeProcessHandle process)
    {
        if (NativeMethods.GetProcessTimes(
                process,
                out var creation,
                out _,
                out _,
                out _) == 0)
        {
            throw ExperimentalSandboxException.FromWin32(
                ExperimentalSandboxOperation.TrackProcess,
                Marshal.GetLastPInvokeError(),
                "Could not capture the new process identity.");
        }

        return creation.ToLong();
    }
}
