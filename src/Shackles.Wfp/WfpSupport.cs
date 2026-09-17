using Shackles.Wfp.Internal;
using Shackles.Wfp.Interop;

namespace Shackles.Wfp;

public static class WfpSupport
{
    public static bool IsCurrentProcessHighIntegrity() =>
        WfpProcessElevation.IsCurrentProcessHighIntegrity();

    public static WfpSupportInfo Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WfpSupportInfo(
                IsAvailable: false,
                IsHighIntegrity: false,
                "Windows Filtering Platform is available only on Windows.");
        }

        bool elevated;
        try
        {
            elevated = WfpProcessElevation.IsCurrentProcessHighIntegrity();
        }
        catch (WfpException exception)
        {
            return new WfpSupportInfo(false, false, exception.Message);
        }

        using var memory = new UnmanagedMemoryScope();
        var sessionId = Guid.NewGuid();
        var session = new NativeSession
        {
            SessionKey = sessionId,
            DisplayData = new NativeDisplayData
            {
                Name = memory.AllocateString($"Shackles WFP support probe {sessionId:N}"),
                Description = memory.AllocateString("Read-only WFP availability probe from Shackles.")
            },
            Flags = WfpNativeConstants.DynamicSession
        };

        var result = NativeMethods.FwpmEngineOpen(
            null,
            WfpNativeConstants.RpcAuthenticationWinNt,
            0,
            in session,
            out var engine);
        if (result != 0)
        {
            return new WfpSupportInfo(
                false,
                elevated,
                WfpException.FromNativeError(
                    WfpOperation.CheckSupport,
                    result,
                    "Shackles could not connect to the Base Filtering Engine").Message);
        }

        var closeResult = NativeMethods.FwpmEngineClose(engine);
        if (closeResult != 0)
        {
            return new WfpSupportInfo(
                false,
                elevated,
                WfpException.FromNativeError(
                    WfpOperation.CheckSupport,
                    closeResult,
                    "Shackles connected to WFP but could not close its support probe").Message);
        }

        return new WfpSupportInfo(
            true,
            elevated,
            elevated
                ? "The Base Filtering Engine is available and Shackles is elevated."
                : "The Base Filtering Engine is available; administrator access is required to change policy.");
    }
}
