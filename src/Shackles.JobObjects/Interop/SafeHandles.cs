using Microsoft.Win32.SafeHandles;

namespace Shackles.JobObjects.Interop;

internal abstract class SafeKernelObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    protected SafeKernelObjectHandle()
        : base(ownsHandle: true)
    {
    }

    protected SafeKernelObjectHandle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle) != 0;
}

internal sealed class SafeJobHandle : SafeKernelObjectHandle
{
    internal SafeJobHandle()
    {
    }

    internal SafeJobHandle(nint handle)
        : base(handle)
    {
    }
}

internal sealed class SafeProcessHandle : SafeKernelObjectHandle
{
    internal SafeProcessHandle()
    {
    }

    internal SafeProcessHandle(nint handle)
        : base(handle)
    {
    }
}

internal sealed class SafeThreadHandle : SafeKernelObjectHandle
{
    internal SafeThreadHandle()
    {
    }

    internal SafeThreadHandle(nint handle)
        : base(handle)
    {
    }
}

internal sealed class SafeCompletionPortHandle : SafeKernelObjectHandle
{
    internal SafeCompletionPortHandle()
    {
    }

    internal SafeCompletionPortHandle(nint handle)
        : base(handle)
    {
    }
}
