using Microsoft.Win32.SafeHandles;

namespace Shackles.AppContainers.Interop;

internal abstract class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    protected SafeKernelHandle()
        : base(ownsHandle: true)
    {
    }

    protected SafeKernelHandle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle) != 0;
}

internal sealed class SafeProcessHandle : SafeKernelHandle
{
    internal SafeProcessHandle(nint handle)
        : base(handle)
    {
    }
}

internal sealed class SafeThreadHandle : SafeKernelHandle
{
    internal SafeThreadHandle(nint handle)
        : base(handle)
    {
    }
}

internal sealed class SafeTokenHandle : SafeKernelHandle
{
    internal SafeTokenHandle(nint handle)
        : base(handle)
    {
    }
}

internal sealed class SafeRegistryKeyHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeRegistryKeyHandle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => NativeMethods.RegCloseKey(handle) == 0;
}
