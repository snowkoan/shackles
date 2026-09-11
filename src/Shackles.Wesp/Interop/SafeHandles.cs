using Microsoft.Win32.SafeHandles;

namespace Shackles.Wesp.Interop;

internal abstract class SafeKernelObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    protected SafeKernelObjectHandle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}

internal sealed class SafeProcessHandle(nint handle) : SafeKernelObjectHandle(handle);

internal sealed class SafeThreadHandle(nint handle) : SafeKernelObjectHandle(handle);

internal sealed class SafeTokenHandle(nint handle) : SafeKernelObjectHandle(handle);

internal sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeServiceHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => NativeMethods.CloseServiceHandle(handle);
}
