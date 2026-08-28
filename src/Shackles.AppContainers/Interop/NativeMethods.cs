using System.Runtime.InteropServices;

namespace Shackles.AppContainers.Interop;

internal static partial class NativeMethods
{
    private const string Advapi32 = "advapi32.dll";
    private const string Kernel32 = "kernel32.dll";
    private const string KernelBase = "kernelbase.dll";
    private const string Userenv = "userenv.dll";

    [LibraryImport(Userenv, EntryPoint = "CreateAppContainerProfile", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int CreateAppContainerProfile(
        string appContainerName,
        string displayName,
        string description,
        nint capabilities,
        uint capabilityCount,
        out nint appContainerSid);

    [LibraryImport(Userenv, EntryPoint = "DeriveAppContainerSidFromAppContainerName", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int DeriveAppContainerSidFromAppContainerName(
        string appContainerName,
        out nint appContainerSid);

    [LibraryImport(Userenv, EntryPoint = "DeleteAppContainerProfile", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int DeleteAppContainerProfile(string appContainerName);

    [LibraryImport(KernelBase, EntryPoint = "DeriveCapabilitySidsFromName", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int DeriveCapabilitySidsFromName(
        string capabilityName,
        out nint capabilityGroupSids,
        out uint capabilityGroupSidCount,
        out nint capabilitySids,
        out uint capabilitySidCount);

    [LibraryImport(Advapi32)]
    internal static partial nint FreeSid(nint sid);

    [LibraryImport(Advapi32)]
    internal static partial int EqualSid(nint firstSid, nint secondSid);

    [LibraryImport(Kernel32)]
    internal static partial nint LocalFree(nint memory);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int InitializeProcThreadAttributeList(
        nint attributeList,
        uint attributeCount,
        uint flags,
        ref nuint size);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int UpdateProcThreadAttribute(
        nint attributeList,
        uint flags,
        nuint attribute,
        nint value,
        nuint size,
        nint previousValue,
        nint returnSize);

    [LibraryImport(Kernel32)]
    internal static partial void DeleteProcThreadAttributeList(nint attributeList);

    [LibraryImport(Kernel32, EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial int CreateProcess(
        string? applicationName,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        int inheritHandles,
        ProcessCreationFlags creationFlags,
        nint environment,
        string? currentDirectory,
        NativeStartupInfoEx* startupInfo,
        NativeProcessInformation* processInformation);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int TerminateProcess(SafeProcessHandle process, uint exitCode);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial uint WaitForSingleObject(
        SafeProcessHandle handle,
        uint milliseconds);

    [LibraryImport(Kernel32, EntryPoint = "GetExitCodeProcess", SetLastError = true)]
    internal static partial int GetExitCodeProcessRaw(
        nint process,
        out uint exitCode);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int GetProcessTimes(
        SafeProcessHandle process,
        out NativeFileTime creationTime,
        out NativeFileTime exitTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int CloseHandle(nint handle);

    [LibraryImport(Kernel32)]
    internal static partial nint GetCurrentProcess();

    [LibraryImport(Userenv, SetLastError = true)]
    internal static partial int CreateEnvironmentBlock(
        out nint environment,
        nint token,
        int inherit);

    [LibraryImport(Userenv)]
    internal static partial int DestroyEnvironmentBlock(nint environment);

    [LibraryImport(Advapi32, EntryPoint = "OpenProcessToken", SetLastError = true)]
    internal static partial int OpenProcessTokenRaw(
        nint process,
        uint desiredAccess,
        out nint token);

    [LibraryImport(Advapi32, EntryPoint = "GetNamedSecurityInfoW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetNamedSecurityInfo(
        string objectName,
        SecurityObjectType objectType,
        SecurityInformation securityInformation,
        out nint owner,
        out nint group,
        out nint dacl,
        out nint sacl,
        out nint securityDescriptor);

    [LibraryImport(Advapi32, EntryPoint = "SetNamedSecurityInfoW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint SetNamedSecurityInfo(
        string objectName,
        SecurityObjectType objectType,
        SecurityInformation securityInformation,
        nint owner,
        nint group,
        nint dacl,
        nint sacl);

    [LibraryImport(Advapi32, EntryPoint = "SetEntriesInAclW")]
    internal static unsafe partial uint SetEntriesInAcl(
        uint entryCount,
        NativeExplicitAccess* explicitEntries,
        nint oldAcl,
        out nint newAcl);

    [LibraryImport(Advapi32)]
    internal static partial uint GetSecurityInfo(
        SafeRegistryKeyHandle handle,
        SecurityObjectType objectType,
        SecurityInformation securityInformation,
        out nint owner,
        out nint group,
        out nint dacl,
        out nint sacl,
        out nint securityDescriptor);

    [LibraryImport(Advapi32)]
    internal static partial uint SetSecurityInfo(
        SafeRegistryKeyHandle handle,
        SecurityObjectType objectType,
        SecurityInformation securityInformation,
        nint owner,
        nint group,
        nint dacl,
        nint sacl);

    [LibraryImport(Advapi32, EntryPoint = "RegOpenKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegOpenKeyEx(
        nint rootKey,
        string subKey,
        uint options,
        uint desiredAccess,
        out nint result);

    [LibraryImport(Advapi32)]
    internal static partial int RegCloseKey(nint key);

    [LibraryImport(Advapi32)]
    internal static partial int GetAce(nint acl, uint index, out nint ace);

    [LibraryImport(Advapi32, SetLastError = true)]
    internal static partial int InitializeAcl(
        nint acl,
        uint aclLength,
        uint aclRevision);

    [LibraryImport(Advapi32, SetLastError = true)]
    internal static partial int AddAce(
        nint acl,
        uint aceRevision,
        uint startingAceIndex,
        nint aceList,
        uint aceListLength);
}
