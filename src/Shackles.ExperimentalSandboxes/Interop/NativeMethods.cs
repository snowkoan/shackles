using System.Runtime.InteropServices;

namespace Shackles.ExperimentalSandboxes.Interop;

internal static partial class NativeMethods
{
    private const string Advapi32 = "advapi32.dll";
    private const string Kernel32 = "kernel32.dll";
    private const string Ntdll = "ntdll.dll";
    private const string Userenv = "userenv.dll";

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int CloseHandle(nint handle);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int TerminateProcess(
        SafeProcessHandle process,
        uint exitCode);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial uint WaitForSingleObject(
        SafeProcessHandle process,
        uint milliseconds);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int GetProcessTimes(
        SafeProcessHandle process,
        out NativeFileTime creationTime,
        out NativeFileTime exitTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [LibraryImport(Kernel32)]
    internal static partial nint GetCurrentProcess();

    [LibraryImport(Advapi32, EntryPoint = "OpenProcessToken", SetLastError = true)]
    internal static partial int OpenProcessToken(
        nint process,
        uint desiredAccess,
        out nint token);

    [LibraryImport(Userenv, SetLastError = true)]
    internal static partial int CreateEnvironmentBlock(
        out nint environment,
        nint token,
        int inherit);

    [LibraryImport(Userenv)]
    internal static partial int DestroyEnvironmentBlock(nint environment);

    [LibraryImport(Userenv, EntryPoint = "DeriveAppContainerSidFromAppContainerName", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int DeriveAppContainerSidFromAppContainerName(
        string appContainerName,
        out nint appContainerSid);

    [LibraryImport(Userenv, EntryPoint = "DeleteAppContainerProfile", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int DeleteAppContainerProfile(string appContainerName);

    [LibraryImport(Advapi32)]
    internal static partial nint FreeSid(nint sid);

    [LibraryImport(Ntdll)]
    internal static partial int RtlQueryFeatureConfiguration(
        uint featureId,
        uint configurationType,
        ref ulong changeStamp,
        out NativeFeatureConfiguration featureConfiguration);
}
