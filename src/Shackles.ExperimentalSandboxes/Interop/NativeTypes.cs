using System.Runtime.InteropServices;

namespace Shackles.ExperimentalSandboxes.Interop;

[Flags]
internal enum ProcessCreationFlags : uint
{
    None = 0,
    UnicodeEnvironment = 0x00000400
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeStartupInfo
{
    internal uint Size;
    internal nint Reserved;
    internal nint Desktop;
    internal nint Title;
    internal uint X;
    internal uint Y;
    internal uint XSize;
    internal uint YSize;
    internal uint XCountChars;
    internal uint YCountChars;
    internal uint FillAttribute;
    internal uint Flags;
    internal ushort ShowWindow;
    internal ushort Reserved2Size;
    internal nint Reserved2;
    internal nint StandardInput;
    internal nint StandardOutput;
    internal nint StandardError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeProcessInformation
{
    internal nint Process;
    internal nint Thread;
    internal uint ProcessId;
    internal uint ThreadId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFileTime
{
    internal uint LowDateTime;
    internal uint HighDateTime;

    internal long ToLong() =>
        unchecked((long)(((ulong)HighDateTime << 32) | LowDateTime));
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFeatureConfiguration
{
    internal uint FeatureId;
    internal uint CompactState;
    internal uint VariantPayload;
}

[UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
internal unsafe delegate int CreateProcessInSandboxDelegate(
    nint applicationName,
    char* commandLine,
    nint processAttributes,
    nint threadAttributes,
    int inheritHandles,
    ProcessCreationFlags creationFlags,
    nint environment,
    nint currentDirectory,
    NativeStartupInfo* startupInfo,
    nint identity,
    byte* sandboxSpecification,
    uint sandboxSpecificationSize,
    NativeProcessInformation* processInformation);

[UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
internal unsafe delegate int QuerySandboxSupportDelegate(ulong* capabilities);
