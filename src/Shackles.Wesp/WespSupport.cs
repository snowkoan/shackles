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
                "The bundled WESP client currently supports only x64 processes.",
                null,
                Array.Empty<string>());
        }

        string libraryPath;
        try
        {
            libraryPath = WespClientLibrary.GetCandidatePath();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new WespSupportInfo(
                WespAvailability.ProbeFailed,
                $"The configured WESP client path is invalid: {exception.Message}",
                null,
                Array.Empty<string>());
        }

        if (!File.Exists(libraryPath))
        {
            return new WespSupportInfo(
                WespAvailability.ClientLibraryMissing,
                $"WESP is not installed. {NativeMethods.WespLibrary} was not found at '{libraryPath}'.",
                libraryPath,
                Array.Empty<string>());
        }

        nint library = 0;
        try
        {
            if (!NativeLibrary.TryLoad(libraryPath, out library))
            {
                return new WespSupportInfo(
                    WespAvailability.ProbeFailed,
                    "The WESP client library is present but Windows could not load it.",
                    libraryPath,
                    Array.Empty<string>());
            }

            var missing = requiredExports
                .Where(export => !NativeLibrary.TryGetExport(library, export, out _))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (missing.Length != 0)
            {
                return new WespSupportInfo(
                    WespAvailability.RequiredExportMissing,
                    missingExportSummary,
                    libraryPath,
                    missing);
            }

            return new WespSupportInfo(
                WespAvailability.Available,
                "WESP client API found. Driver compatibility and the capabilities required by your rules will be checked when you launch.",
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
}
