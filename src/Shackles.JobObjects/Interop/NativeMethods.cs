using System.Runtime.InteropServices;

namespace Shackles.JobObjects.Interop;

internal static partial class NativeMethods
{
    private const string Kernel32 = "kernel32.dll";

    [LibraryImport(Kernel32, EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateJobObject(nint jobAttributes, string? name);

    [LibraryImport(Kernel32, EntryPoint = "OpenJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint OpenJobObject(JobAccessRights desiredAccess, int inheritHandle, string name);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static unsafe partial int QueryInformationJobObject(
        SafeJobHandle job,
        JobObjectInformationClass informationClass,
        void* information,
        uint informationLength,
        uint* returnLength);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static unsafe partial int SetInformationJobObject(
        SafeJobHandle job,
        JobObjectInformationClass informationClass,
        void* information,
        uint informationLength);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial nint OpenProcess(ProcessAccessRights desiredAccess, int inheritHandle, uint processId);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int GetProcessTimes(
        SafeProcessHandle process,
        out NativeFileTime creationTime,
        out NativeFileTime exitTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

    [LibraryImport(Kernel32, EntryPoint = "IsProcessInJob", SetLastError = true)]
    internal static partial int IsProcessInAnyJob(
        SafeProcessHandle process,
        nint job,
        out int result);

    [LibraryImport(Kernel32, EntryPoint = "IsProcessInJob", SetLastError = true)]
    internal static partial int IsProcessInJob(
        SafeProcessHandle process,
        SafeJobHandle job,
        out int result);

    [LibraryImport(Kernel32, EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial int CreateProcess(
        string applicationName,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        int inheritHandles,
        ProcessCreationFlags creationFlags,
        nint environment,
        string? currentDirectory,
        NativeStartupInfo* startupInfo,
        NativeProcessInformation* processInformation);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial uint ResumeThread(SafeThreadHandle thread);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int TerminateProcess(SafeProcessHandle process, uint exitCode);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int CloseHandle(nint handle);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial nint CreateIoCompletionPort(
        nint fileHandle,
        nint existingCompletionPort,
        nuint completionKey,
        uint numberOfConcurrentThreads);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int GetQueuedCompletionStatus(
        SafeCompletionPortHandle completionPort,
        out uint numberOfBytesTransferred,
        out nuint completionKey,
        out nint overlapped,
        uint milliseconds);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial int PostQueuedCompletionStatus(
        SafeCompletionPortHandle completionPort,
        uint numberOfBytesTransferred,
        nuint completionKey,
        nint overlapped);

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial ushort GetActiveProcessorGroupCount();

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial uint GetActiveProcessorCount(ushort groupNumber);
}
