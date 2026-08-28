using System.Runtime.InteropServices;
using System.Text;
using Shackles.AppContainers.Interop;

namespace Shackles.AppContainers.Internal;

internal static class AppContainerLauncher
{
    private const nuint ProcThreadAttributeSecurityCapabilities = 0x00020009;
    private const nuint ProcThreadAttributeChildProcessPolicy = 0x0002000E;
    private const nuint ProcThreadAttributeAllApplicationPackagesPolicy = 0x0002000F;
    private const uint ProcessCreationChildProcessRestricted = 1;
    private const uint ProcessCreationAllApplicationPackagesOptOut = 1;
    private const uint TokenQuery = 0x0008;

    internal static unsafe TrackedAppContainerProcess Launch(
        AppContainerIdentity identity,
        IReadOnlyList<byte[]> capabilitySids,
        AppContainerSandboxOptions sandboxOptions,
        AppContainerLaunchOptions launchOptions,
        IReadOnlyList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(capabilitySids);
        ArgumentNullException.ThrowIfNull(sandboxOptions);
        ArgumentNullException.ThrowIfNull(launchOptions);
        ArgumentNullException.ThrowIfNull(warnings);

        var executablePath = ValidateExecutable(launchOptions.FileName);
        var workingDirectory =
            ValidateWorkingDirectory(launchOptions.WorkingDirectory);
        var arguments = launchOptions.Arguments ??
            throw new ArgumentException(
                "Arguments cannot be null.",
                nameof(launchOptions));
        if (arguments.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Arguments cannot contain a null character.",
                nameof(launchOptions));
        }

        var commandLine = string.Concat(
            QuoteArgument(executablePath),
            string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments);
        var mutableCommandLine = string.Concat(commandLine, '\0').ToCharArray();

        var sidAllocations = new List<nint>();
        nint capabilityArray = 0;
        nint securityCapabilities = 0;
        nint attributeList = 0;
        nint environment = 0;
        var attributeListInitialized = false;
        SafeProcessHandle? process = null;
        SafeThreadHandle? thread = null;
        try
        {
            var appContainerSid = CopyToNative(identity.SidBytes);
            sidAllocations.Add(appContainerSid);

            if (capabilitySids.Count > 0)
            {
                capabilityArray = Marshal.AllocHGlobal(
                    checked(capabilitySids.Count * sizeof(NativeSidAndAttributes)));
                for (var index = 0; index < capabilitySids.Count; index++)
                {
                    var capabilitySid = CopyToNative(capabilitySids[index]);
                    sidAllocations.Add(capabilitySid);
                    Marshal.StructureToPtr(
                        new NativeSidAndAttributes
                        {
                            Sid = capabilitySid,
                            Attributes = 0x00000004
                        },
                        capabilityArray +
                        (index * sizeof(NativeSidAndAttributes)),
                        false);
                }
            }

            securityCapabilities =
                Marshal.AllocHGlobal(sizeof(NativeSecurityCapabilities));
            Marshal.StructureToPtr(
                new NativeSecurityCapabilities
                {
                    AppContainerSid = appContainerSid,
                    Capabilities = capabilityArray,
                    CapabilityCount = checked((uint)capabilitySids.Count)
                },
                securityCapabilities,
                false);

            var attributeCount = 1u;
            attributeCount +=
                sandboxOptions.RestrictChildProcessCreation ? 1u : 0u;
            attributeCount +=
                sandboxOptions.IsolationMode ==
                AppContainerIsolationMode.LowPrivilege
                    ? 1u
                    : 0u;
            nuint attributeListSize = 0;
            _ = NativeMethods.InitializeProcThreadAttributeList(
                0,
                attributeCount,
                0,
                ref attributeListSize);
            if (attributeListSize == 0)
            {
                throw AppContainerException.FromWin32(
                    AppContainerOperation.CreateProcess,
                    Marshal.GetLastPInvokeError(),
                    "Windows did not report a process attribute-list size.");
            }

            attributeList =
                Marshal.AllocHGlobal(checked((nint)attributeListSize));
            if (NativeMethods.InitializeProcThreadAttributeList(
                    attributeList,
                    attributeCount,
                    0,
                    ref attributeListSize) == 0)
            {
                throw AppContainerException.FromWin32(
                    AppContainerOperation.CreateProcess,
                    Marshal.GetLastPInvokeError(),
                    "Could not initialize the process attribute list.");
            }

            attributeListInitialized = true;
            AddAttribute(
                attributeList,
                ProcThreadAttributeSecurityCapabilities,
                securityCapabilities,
                checked((nuint)sizeof(NativeSecurityCapabilities)));

            var childPolicy = ProcessCreationChildProcessRestricted;
            if (sandboxOptions.RestrictChildProcessCreation)
            {
                AddAttribute(
                    attributeList,
                    ProcThreadAttributeChildProcessPolicy,
                    (nint)(&childPolicy),
                    sizeof(uint));
            }

            var packagePolicy = ProcessCreationAllApplicationPackagesOptOut;
            if (sandboxOptions.IsolationMode ==
                AppContainerIsolationMode.LowPrivilege)
            {
                AddAttribute(
                    attributeList,
                    ProcThreadAttributeAllApplicationPackagesPolicy,
                    (nint)(&packagePolicy),
                    sizeof(uint));
            }

            var startup = new NativeStartupInfoEx
            {
                StartupInfo = new NativeStartupInfo
                {
                    Size = checked((uint)sizeof(NativeStartupInfoEx))
                },
                AttributeList = attributeList
            };
            var processInformation = new NativeProcessInformation();
            var creationFlags =
                ProcessCreationFlags.ExtendedStartupInfoPresent;
            if (sandboxOptions.UseMinimalEnvironment)
            {
                if (NativeMethods.OpenProcessTokenRaw(
                        NativeMethods.GetCurrentProcess(),
                        TokenQuery,
                        out var rawEnvironmentToken) == 0)
                {
                    throw AppContainerException.FromWin32(
                        AppContainerOperation.CreateProcess,
                        Marshal.GetLastPInvokeError(),
                        "Windows could not open the current user token for a clean environment.");
                }

                using var environmentToken =
                    new SafeTokenHandle(rawEnvironmentToken);
                if (NativeMethods.CreateEnvironmentBlock(
                        out environment,
                        environmentToken.DangerousGetHandle(),
                        inherit: 0) == 0)
                {
                    throw AppContainerException.FromWin32(
                        AppContainerOperation.CreateProcess,
                        Marshal.GetLastPInvokeError(),
                        "Windows could not create the clean user environment block.");
                }

                creationFlags |= ProcessCreationFlags.UnicodeEnvironment;
            }

            fixed (char* commandLinePointer = mutableCommandLine)
            {
                if (NativeMethods.CreateProcess(
                        applicationName: null,
                        commandLinePointer,
                        0,
                        0,
                        inheritHandles: 0,
                        creationFlags,
                        environment,
                        workingDirectory,
                        &startup,
                        &processInformation) == 0)
                {
                    throw AppContainerException.FromWin32(
                        AppContainerOperation.CreateProcess,
                        Marshal.GetLastPInvokeError(),
                        $"Windows could not start '{executablePath}' in the AppContainer.");
                }
            }

            process = new SafeProcessHandle(processInformation.Process);
            thread = new SafeThreadHandle(processInformation.Thread);
            var processId = checked((int)processInformation.ProcessId);
            var creationTime = ReadCreationTime(process);
            var tracked = new TrackedAppContainerProcess(
                process,
                new AppContainerLaunchResult(
                    processId,
                    creationTime,
                    warnings.ToArray()));
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
            if (attributeListInitialized)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
            }

            if (attributeList != 0)
            {
                Marshal.FreeHGlobal(attributeList);
            }

            if (securityCapabilities != 0)
            {
                Marshal.FreeHGlobal(securityCapabilities);
            }

            if (capabilityArray != 0)
            {
                Marshal.FreeHGlobal(capabilityArray);
            }

            foreach (var allocation in sidAllocations)
            {
                Marshal.FreeHGlobal(allocation);
            }

            if (environment != 0)
            {
                _ = NativeMethods.DestroyEnvironmentBlock(environment);
            }
        }
    }

    private static void AddAttribute(
        nint attributeList,
        nuint attribute,
        nint value,
        nuint size)
    {
        if (NativeMethods.UpdateProcThreadAttribute(
                attributeList,
                0,
                attribute,
                value,
                size,
                0,
                0) == 0)
        {
            throw AppContainerException.FromWin32(
                AppContainerOperation.CreateProcess,
                Marshal.GetLastPInvokeError(),
                $"Could not install process attribute 0x{attribute:X}.");
        }
    }

    private static string ValidateExecutable(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A valid executable path is required.",
                nameof(fileName));
        }

        var path = Path.GetFullPath(fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The executable was not found.",
                path);
        }

        return path;
    }

    private static string? ValidateWorkingDirectory(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        if (requested.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The working directory cannot contain a null character.",
                nameof(requested));
        }

        var fullPath = Path.GetFullPath(requested);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"The working directory does not exist: {fullPath}");
        }

        return fullPath;
    }

    private static nint CopyToNative(byte[] bytes)
    {
        var allocation = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, allocation, bytes.Length);
        return allocation;
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
            throw AppContainerException.FromWin32(
                AppContainerOperation.TrackProcess,
                Marshal.GetLastPInvokeError(),
                "Could not capture the new process identity.");
        }

        return creation.ToLong();
    }
}
