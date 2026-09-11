using System.Runtime.InteropServices;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Internal;

internal enum WespDriverServiceStatus
{
    Unknown,
    Running,
    NotRunning,
    NotInstalled
}

internal static class WespDriverStatusProbe
{
    private const string WespDriverServiceName = "wesp";
    private const uint ServiceControlManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceRunning = 0x00000004;
    private const int ErrorServiceDoesNotExist = 1060;

    internal static WespDriverServiceStatus GetStatus()
    {
        if (!OperatingSystem.IsWindows())
        {
            return WespDriverServiceStatus.Unknown;
        }

        try
        {
            using var manager = NativeMethods.OpenServiceControlManager(
                machineName: null,
                databaseName: null,
                ServiceControlManagerConnect);
            if (manager.IsInvalid)
            {
                return WespDriverServiceStatus.Unknown;
            }

            using var service = NativeMethods.OpenService(
                manager,
                WespDriverServiceName,
                ServiceQueryStatus);
            if (service.IsInvalid)
            {
                return Marshal.GetLastPInvokeError() == ErrorServiceDoesNotExist
                    ? WespDriverServiceStatus.NotInstalled
                    : WespDriverServiceStatus.Unknown;
            }

            return NativeMethods.QueryServiceStatus(service, out var status)
                ? status.CurrentState == ServiceRunning
                    ? WespDriverServiceStatus.Running
                    : WespDriverServiceStatus.NotRunning
                : WespDriverServiceStatus.Unknown;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return WespDriverServiceStatus.Unknown;
        }
    }
}
