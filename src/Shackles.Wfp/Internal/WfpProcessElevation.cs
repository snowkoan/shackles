using System.Runtime.InteropServices;
using Shackles.Wfp.Interop;

namespace Shackles.Wfp.Internal;

internal static class WfpProcessElevation
{
    internal static bool IsCurrentProcessHighIntegrity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                WfpNativeConstants.TokenQuery,
                out var token))
        {
            throw FromLastError(
                "Windows could not open the current process token to check administrator access.");
        }

        try
        {
            var size = checked((uint)Marshal.SizeOf<NativeTokenElevation>());
            if (!NativeMethods.GetTokenInformation(
                    token,
                    WfpNativeConstants.TokenElevation,
                    out var elevation,
                    size,
                    out _))
            {
                throw FromLastError(
                    "Windows could not read the current process elevation state.");
            }

            return elevation.TokenIsElevated != 0;
        }
        finally
        {
            _ = NativeMethods.CloseHandle(token);
        }
    }

    internal static void EnsureHighIntegrity()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new WfpException(
                WfpOperation.CheckIntegrity,
                "WFP Blocking is supported only on Windows.");
        }

        if (!IsCurrentProcessHighIntegrity())
        {
            throw new WfpException(
                WfpOperation.CheckIntegrity,
                "WFP policy changes require an elevated Shackles process. Open the WFP workspace as administrator.");
        }
    }

    private static WfpException FromLastError(string detail) =>
        WfpException.FromNativeError(
            WfpOperation.CheckIntegrity,
            checked((uint)Marshal.GetLastWin32Error()),
            detail);
}
