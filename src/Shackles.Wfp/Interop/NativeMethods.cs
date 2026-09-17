using System.Runtime.InteropServices;

namespace Shackles.Wfp.Interop;

internal static partial class NativeMethods
{
    private const string Fwpuclnt = "fwpuclnt.dll";
    private const string Advapi32 = "advapi32.dll";
    private const string Kernel32 = "kernel32.dll";
    private const string Iphlpapi = "iphlpapi.dll";

    [DllImport(Fwpuclnt, EntryPoint = "FwpmEngineOpen0", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern uint FwpmEngineOpen(
        string? serverName,
        uint authenticationService,
        nint authenticationIdentity,
        in NativeSession session,
        out nint engineHandle);

    [DllImport(Fwpuclnt, EntryPoint = "FwpmEngineClose0", ExactSpelling = true)]
    internal static extern uint FwpmEngineClose(nint engineHandle);

    [DllImport(Fwpuclnt, EntryPoint = "FwpmTransactionBegin0", ExactSpelling = true)]
    internal static extern uint FwpmTransactionBegin(nint engineHandle, uint flags);

    [DllImport(Fwpuclnt, EntryPoint = "FwpmTransactionCommit0", ExactSpelling = true)]
    internal static extern uint FwpmTransactionCommit(nint engineHandle);

    [DllImport(Fwpuclnt, EntryPoint = "FwpmTransactionAbort0", ExactSpelling = true)]
    internal static extern uint FwpmTransactionAbort(nint engineHandle);

    [DllImport(Fwpuclnt, EntryPoint = "FwpmProviderAdd0", ExactSpelling = true)]
    internal static extern uint FwpmProviderAdd(
        nint engineHandle,
        in NativeProvider provider,
        nint securityDescriptor);

    [DllImport(Fwpuclnt, EntryPoint = "FwpmProviderDeleteByKey0", ExactSpelling = true)]
    internal static extern uint FwpmProviderDeleteByKey(
        nint engineHandle,
        in Guid providerKey);

    [DllImport(Fwpuclnt, EntryPoint = "FwpmSubLayerAdd0", ExactSpelling = true)]
    internal static extern uint FwpmSubLayerAdd(
        nint engineHandle,
        in NativeSubLayer subLayer,
        nint securityDescriptor);

    [DllImport(Fwpuclnt, EntryPoint = "FwpmSubLayerDeleteByKey0", ExactSpelling = true)]
    internal static extern uint FwpmSubLayerDeleteByKey(
        nint engineHandle,
        in Guid subLayerKey);

    [DllImport(Fwpuclnt, EntryPoint = "FwpmFilterAdd0", ExactSpelling = true)]
    internal static extern uint FwpmFilterAdd(
        nint engineHandle,
        in NativeFilter filter,
        nint securityDescriptor,
        out ulong filterId);

    [DllImport(Fwpuclnt, EntryPoint = "FwpmFilterDeleteByKey0", ExactSpelling = true)]
    internal static extern uint FwpmFilterDeleteByKey(
        nint engineHandle,
        in Guid filterKey);

    [DllImport(Fwpuclnt, EntryPoint = "FwpmGetAppIdFromFileName0", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern uint FwpmGetAppIdFromFileName(
        string fileName,
        out nint appId);

    [DllImport(Fwpuclnt, EntryPoint = "FwpmFreeMemory0", ExactSpelling = true)]
    internal static extern void FwpmFreeMemory(ref nint memory);

    [DllImport(Iphlpapi, ExactSpelling = true)]
    internal static extern uint ConvertInterfaceGuidToLuid(
        in Guid interfaceGuid,
        out ulong interfaceLuid);

    [DllImport(Advapi32, EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSecurityDescriptorRevision,
        out nint securityDescriptor,
        out uint securityDescriptorSize);

    [DllImport(Kernel32)]
    internal static extern nint LocalFree(nint memory);

    [DllImport(Kernel32)]
    internal static extern nint GetCurrentProcess();

    [DllImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport(Advapi32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(
        nint processHandle,
        uint desiredAccess,
        out nint tokenHandle);

    [DllImport(Advapi32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(
        nint tokenHandle,
        uint tokenInformationClass,
        out NativeTokenElevation tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);
}
