using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Shackles.ExperimentalSandboxes.Interop;

namespace Shackles.ExperimentalSandboxes.Internal;

internal static class SandboxSupportProbe
{
    internal const uint CoreFeatureId = 61389575;
    internal const uint SpecificationFeatureId = 61155944;
    internal const ulong CreateCapability = 0x1;
    private const int ErrorCallNotImplemented = 120;
    private const int ENotImplemented = unchecked((int)0x80004001);
    private const uint BootFeatureConfiguration = 0;
    private const uint RuntimeFeatureConfiguration = 1;

    private static readonly (uint Id, string Name)[] RequiredFeatureIds =
    [
        (CoreFeatureId, "BaseContainer core"),
        (SpecificationFeatureId, "BaseContainer sandbox specification")
    ];

    internal static unsafe ExperimentalSandboxSupport Probe()
    {
        var osVersion = Environment.OSVersion.Version;
        var features = ReadRequiredFeatureStates();
        var processModelVersion = ReadProcessModelVersion();
        if (!OperatingSystem.IsWindows())
        {
            return Create(
                ExperimentalSandboxAvailability.PlatformNotSupported,
                "Experimental process sandboxes are available only on Windows.",
                osVersion,
                processModelVersion,
                false,
                false,
                null,
                null,
                features);
        }

        if (!SandboxNativeApi.TryLoad(
                out var loaded,
                out var failure,
                out var libraryPresent,
                out var createExportPresent))
        {
            var availability = !libraryPresent
                ? ExperimentalSandboxAvailability.LibraryMissing
                : !createExportPresent
                    ? ExperimentalSandboxAvailability.EntryPointMissing
                    : ExperimentalSandboxAvailability.ProbeFailed;
            return Create(
                availability,
                failure ?? "The experimental sandbox API could not be loaded.",
                osVersion,
                processModelVersion,
                createExportPresent,
                false,
                null,
                null,
                features);
        }

        using var api = loaded!;
        if (api.Query is not null)
        {
            ulong capabilities = 0;
            Marshal.SetLastPInvokeError(0);
            var queried = api.Query(&capabilities);
            if (queried != 0)
            {
                var available = (capabilities & CreateCapability) != 0;
                return Create(
                    available
                        ? ExperimentalSandboxAvailability.Available
                        : ExperimentalSandboxAvailability.FeatureDisabled,
                    available
                        ? "Windows reports that Experimental_CreateProcessInSandbox is available."
                        : BuildDisabledSummary(features, null),
                    osVersion,
                    processModelVersion,
                    true,
                    true,
                    capabilities,
                    null,
                    features);
            }
        }

        var processInformation = new NativeProcessInformation();
        Marshal.SetLastPInvokeError(0);
        var result = api.Create(
            0,
            null,
            0,
            0,
            0,
            ProcessCreationFlags.None,
            0,
            0,
            null,
            0,
            null,
            0,
            &processInformation);
        var error = Marshal.GetLastPInvokeError();
        if (result != 0)
        {
            CloseUnexpectedProbeHandles(processInformation);
            return Create(
                ExperimentalSandboxAvailability.Available,
                "Windows accepted the experimental sandbox create contract.",
                osVersion,
                processModelVersion,
                true,
                api.Query is not null,
                null,
                null,
                features);
        }

        if (error is ErrorCallNotImplemented or ENotImplemented)
        {
            return Create(
                ExperimentalSandboxAvailability.FeatureDisabled,
                BuildDisabledSummary(features, error),
                osVersion,
                processModelVersion,
                true,
                api.Query is not null,
                null,
                error,
                features);
        }

        if (error != 0)
        {
            return Create(
                ExperimentalSandboxAvailability.Available,
                "The API reached normal argument validation, so the experimental " +
                "sandbox feature appears enabled.",
                osVersion,
                processModelVersion,
                true,
                api.Query is not null,
                null,
                error,
                features);
        }

        return Create(
            ExperimentalSandboxAvailability.ProbeFailed,
            "The experimental sandbox API rejected the safe probe without reporting an error.",
            osVersion,
            processModelVersion,
            true,
            api.Query is not null,
            null,
            null,
            features);
    }

    private static ExperimentalSandboxSupport Create(
        ExperimentalSandboxAvailability availability,
        string summary,
        Version osVersion,
        string? processModelVersion,
        bool createExportPresent,
        bool queryExportPresent,
        ulong? capabilityMask,
        int? probeErrorCode,
        IReadOnlyList<ExperimentalFeatureState> features) =>
        new(
            availability,
            summary,
            osVersion,
            processModelVersion,
            createExportPresent,
            queryExportPresent,
            capabilityMask,
            probeErrorCode,
            features);

    private static string BuildDisabledSummary(
        ExperimentalFeatureState[] features,
        int? error)
    {
        var detail = error.HasValue
            ? $"Windows returned {error.Value} ({new Win32Exception(error.Value).Message}). "
            : string.Empty;
        var enabled = features
            .Where(feature =>
                feature.ConfigurationState ==
                ExperimentalFeatureConfigurationState.Enabled)
            .Select(feature => feature.Id.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        var state = enabled.Length == features.Length
            ? "Both known Feature Store configurations are enabled."
            : "The Windows Feature Store does not report both known BaseContainer " +
              "feature IDs as enabled: " +
              string.Join(
                  ", ",
                  features.Select(feature =>
                      $"{feature.Id} ({FormatConfigurationState(feature.ConfigurationState)})")) + ".";
        return detail +
               "processmodel.dll exports Experimental_CreateProcessInSandbox, but " +
               "Windows does not make process sandbox creation available on this " +
               "installation. " +
               state;
    }

    private static string FormatConfigurationState(
        ExperimentalFeatureConfigurationState state) =>
        state switch
        {
            ExperimentalFeatureConfigurationState.Enabled => "enabled",
            ExperimentalFeatureConfigurationState.Disabled => "disabled",
            ExperimentalFeatureConfigurationState.Default => "default",
            _ => "unknown"
        };

    private static ExperimentalFeatureState[] ReadRequiredFeatureStates()
    {
        if (!OperatingSystem.IsWindows())
        {
            return RequiredFeatureIds
                .Select(feature => new ExperimentalFeatureState(
                    feature.Id,
                    feature.Name,
                    ExperimentalFeatureConfigurationState.Unknown,
                    null))
                .ToArray();
        }

        return RequiredFeatureIds
            .Select(feature => ReadFeatureState(feature.Id, feature.Name))
            .ToArray();
    }

    private static ExperimentalFeatureState ReadFeatureState(
        uint id,
        string name)
    {
        var configuration = TryReadFeatureConfiguration(
            id,
            RuntimeFeatureConfiguration) ??
            TryReadFeatureConfiguration(id, BootFeatureConfiguration);
        return configuration.HasValue
            ? new ExperimentalFeatureState(
                id,
                name,
                DecodeFeatureConfigurationState(
                    configuration.Value.CompactState),
                configuration.Value.CompactState & 0xF)
            : new ExperimentalFeatureState(
                id,
                name,
                ExperimentalFeatureConfigurationState.Unknown,
                null);
    }

    private static NativeFeatureConfiguration? TryReadFeatureConfiguration(
        uint id,
        uint configurationType)
    {
        try
        {
            ulong changeStamp = 0;
            return NativeMethods.RtlQueryFeatureConfiguration(
                    id,
                    configurationType,
                    ref changeStamp,
                    out var configuration) == 0
                ? configuration
                : null;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    internal static ExperimentalFeatureConfigurationState
        DecodeFeatureConfigurationState(uint compactState) =>
        ((compactState >> 4) & 0x3) switch
        {
            0 => ExperimentalFeatureConfigurationState.Default,
            1 => ExperimentalFeatureConfigurationState.Disabled,
            2 => ExperimentalFeatureConfigurationState.Enabled,
            _ => ExperimentalFeatureConfigurationState.Unknown
        };

    private static string? ReadProcessModelVersion()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "processmodel.dll");
            return File.Exists(path)
                ? FileVersionInfo.GetVersionInfo(path).FileVersion
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void CloseUnexpectedProbeHandles(
        NativeProcessInformation processInformation)
    {
        if (processInformation.Process != 0 && processInformation.Process != -1)
        {
            _ = NativeMethods.CloseHandle(processInformation.Process);
        }

        if (processInformation.Thread != 0 && processInformation.Thread != -1)
        {
            _ = NativeMethods.CloseHandle(processInformation.Thread);
        }
    }
}
