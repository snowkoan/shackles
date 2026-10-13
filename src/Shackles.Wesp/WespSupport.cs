using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Shackles.Wesp.Internal;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp;

public static class WespSupport
{
    private static readonly string[] SessionExports =
    [
        "EspRegisterClient",
        "EspUnregisterClient",
        "EspConnectClient",
        "EspDisconnectClient",
        "EspCreateProcessFilter",
        "EspCloseFilter",
        "EspCreateRule",
        "EspCloseRule",
        "EspUpdateRules",
        "EspRemoveAllRulesForClient",
        "EspRemoveRulesForClient",
        "EspCreateProcessReference",
        "EspGetEventObjectFromReference",
        "EspCloseEventObjectReference",
        "EspSetEventObjectContextKey",
        "EspQueryProcessProperties",
        "EspFreeMemory",
        "EspGetEventCapabilities",
        "EspCreateEventQueue",
        "EspCloseEventQueue",
        "EspConnectEventQueueWithCallback",
        "EspDisconnectEventQueue",
        "EspAllocateEventNotification",
        "EspFreeEventNotification",
        "EspArmEventNotification",
        "EspCompleteEventNotification",
        "EspSetEventQueueStateChangeCallback",
        "EspRemoveEventQueueStateChangeCallback"
    ];

    private static readonly string[] FileExports =
    [
        "EspCreateFileObjectFilter",
        "EspIsFileObjectPropertySupported"
    ];

    private static readonly string[] RegistryExports =
    [
        "EspCreateRegistryKeyFilter",
        "EspIsRegistryKeyPropertySupported"
    ];

    /// <summary>
    /// Determines whether the current process has the integrity level required
    /// to connect a WESP client session.
    /// </summary>
    /// <remarks>
    /// This is an early user-interface check. Session creation verifies the
    /// integrity level again before connecting to WESP.
    /// </remarks>
    public static bool IsCurrentProcessHighIntegrity() =>
        WespProcessIntegrityProbe.IsCurrentProcessHighIntegrity();

    public static WespSupportInfo Probe() => ProbeCore(
        SessionExports,
        "The WESP client does not expose every API needed to connect a blocking session.");

    internal static WespSupportInfo Probe(NormalizedWespPolicy policy)
    {
        return ProbeCore(
            GetRequiredExports(policy),
            "The WESP client does not expose every API required by the configured blocking rules.");
    }

    internal static IReadOnlyCollection<string> GetRequiredExports(NormalizedWespPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var requiredExports = new HashSet<string>(
            SessionExports,
            StringComparer.Ordinal);
        if (policy.BlockedFilePaths.Count != 0 ||
            policy.ReadOnlyFilePaths.Count != 0 ||
            policy.BlockedChildExecutables.Count != 0 ||
            policy.BlockUncPaths)
        {
            requiredExports.UnionWith(FileExports);
        }

        if (policy.BlockedRegistryKeys.Count != 0 ||
            policy.ReadOnlyRegistryKeys.Count != 0)
        {
            requiredExports.UnionWith(RegistryExports);
        }

        if (policy.ReadOnlyFilePaths.Count != 0 ||
            policy.ReadOnlyRegistryKeys.Count != 0)
        {
            requiredExports.Add("EspCreateFilter");
        }

        return requiredExports;
    }

    private static WespSupportInfo ProbeCore(
        IEnumerable<string> requiredExports,
        string missingExportSummary)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WespSupportInfo(
                WespAvailability.PlatformNotSupported,
                "WESP is available only on Windows.",
                null,
                Array.Empty<string>());
        }

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            return new WespSupportInfo(
                WespAvailability.ArchitectureNotSupported,
                "WESP Blocking currently supports only x64 Shackles processes.",
                null,
                Array.Empty<string>());
        }

        return ProbeLibrarySearch(
            Environment.GetEnvironmentVariable(WespClientLibrary.OverrideEnvironmentVariable),
            AppContext.BaseDirectory,
            Environment.SystemDirectory,
            File.Exists,
            path => ProbeClientLibrary(path, requiredExports, missingExportSummary));
    }

    internal static WespSupportInfo ProbeLibrarySearch(
        string? configured,
        string appBaseDirectory,
        string systemDirectory,
        Func<string, bool> fileExists,
        Func<string, WespSupportInfo> inspectLibrary)
    {
        try
        {
            var searchPaths = WespClientLibrary.GetSearchPaths(configured, appBaseDirectory, systemDirectory);
            var useFirstPath = searchPaths.Count == 1 || fileExists(searchPaths[0]);
            var libraryPath = useFirstPath ? searchPaths[0] : searchPaths[1];
            IReadOnlyList<string> checkedPaths = useFirstPath ? [libraryPath] : searchPaths;
            var result = fileExists(libraryPath)
                ? inspectLibrary(libraryPath)
                : new WespSupportInfo(
                    WespAvailability.ClientLibraryMissing,
                    $"The WESP client DLL was not found at '{libraryPath}'.",
                    libraryPath,
                    Array.Empty<string>());
            return result with { CheckedClientLibraryPaths = checkedPaths };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new WespSupportInfo(
                WespAvailability.ProbeFailed,
                $"The configured WESP client path is invalid: {exception.Message}",
                null,
                Array.Empty<string>());
        }

    }

    internal static WespSupportInfo ProbeClientLibrary(
        string libraryPath,
        IEnumerable<string> requiredExports,
        string missingExportSummary)
    {
        if (!File.Exists(libraryPath))
        {
            return new WespSupportInfo(
                WespAvailability.ClientLibraryMissing,
                $"The WESP client DLL was not found at '{libraryPath}'.",
                libraryPath,
                Array.Empty<string>());
        }

        var imageIssue = ProbeClientImage(libraryPath);
        if (imageIssue is not null)
        {
            return imageIssue;
        }

        nint library = 0;
        try
        {
            library = NativeLibrary.Load(libraryPath);

            return InspectLibraryExports(
                libraryPath,
                requiredExports,
                export => NativeLibrary.TryGetExport(library, export, out _),
                missingExportSummary);
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException)
        {
            return new WespSupportInfo(
                WespAvailability.ClientLibraryLoadFailed,
                $"The WESP client DLL is present at '{libraryPath}', but Windows could not load it. " +
                $"Check that the authorized x64 client and its dependencies are available, then refresh support. {exception.Message}",
                libraryPath,
                Array.Empty<string>());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new WespSupportInfo(
                WespAvailability.ProbeFailed,
                $"WESP support could not be checked: {exception.Message}",
                libraryPath,
                Array.Empty<string>());
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }
        }
    }

    internal static WespSupportInfo InspectLibraryExports(
        string libraryPath,
        IEnumerable<string> requiredExports,
        Func<string, bool> hasExport,
        string missingExportSummary)
    {
        var missing = requiredExports.Where(export => !hasExport(export))
            .Order(StringComparer.Ordinal).ToArray();
        return missing.Length != 0
            ? new WespSupportInfo(
                WespAvailability.RequiredExportMissing,
                $"{missingExportSummary} Missing APIs: {string.Join(", ", missing)}. " +
                "Replace it with an authorized x64 WESP client matching the supported release, then refresh support.",
                libraryPath,
                missing)
            : new WespSupportInfo(
                WespAvailability.Available,
                "WESP client API found. Driver compatibility and the capabilities required by your rules will be checked when you start a session.",
                libraryPath,
                Array.Empty<string>());
    }

    internal static WespSupportInfo? ProbeClientImage(string libraryPath)
    {
        try
        {
            using var stream = File.OpenRead(libraryPath);
            using var reader = new PEReader(stream);
            var headers = reader.PEHeaders;
            return CheckClientImage(
                libraryPath,
                headers.PEHeader is not null,
                (headers.CoffHeader.Characteristics & Characteristics.Dll) != 0,
                headers.CoffHeader.Machine,
                headers.PEHeader?.Magic ?? default);
        }
        catch (BadImageFormatException exception)
        {
            return new WespSupportInfo(
                WespAvailability.ClientLibraryInvalid,
                $"The WESP client DLL at '{libraryPath}' is malformed. " +
                $"Replace it with an authorized x64 espclient.dll, then refresh support. {exception.Message}",
                libraryPath,
                Array.Empty<string>());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new WespSupportInfo(
                WespAvailability.ProbeFailed,
                $"The WESP client DLL at '{libraryPath}' could not be inspected: {exception.Message}",
                libraryPath,
                Array.Empty<string>());
        }
    }

    internal static WespSupportInfo? CheckClientImage(
        string libraryPath,
        bool hasPeHeader,
        bool isDll,
        Machine machine,
        PEMagic magic)
    {
        if (!hasPeHeader || !isDll)
        {
            return new WespSupportInfo(
                WespAvailability.ClientLibraryInvalid,
                $"The file at '{libraryPath}' is not a valid Windows DLL. " +
                "Replace it with an authorized x64 espclient.dll, then refresh support.",
                libraryPath,
                Array.Empty<string>());
        }

        return machine != Machine.Amd64 || magic != PEMagic.PE32Plus
            ? new WespSupportInfo(
                WespAvailability.ClientLibraryArchitectureNotSupported,
                $"The WESP client DLL at '{libraryPath}' is not x64 ({machine}). " +
                "Use the authorized x64 espclient.dll, then refresh support.",
                libraryPath,
                Array.Empty<string>())
            : null;
    }
}
