using System.Runtime.InteropServices;

namespace Shackles.Wesp.Interop;

internal static class NativeMethods
{
    internal const string WespLibrary = "espclient.dll";
    private const string Kernel32 = "kernel32.dll";
    private const string Advapi32 = "advapi32.dll";

    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(NativeMethods).Assembly,
            static (libraryName, _, _) =>
                string.Equals(libraryName, WespLibrary, StringComparison.OrdinalIgnoreCase)
                    ? NativeLibrary.Load(WespClientLibrary.GetCandidatePath())
                    : 0);
    }

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspRegisterClient(in NativeClientDescriptor descriptor);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspUnregisterClient(in Guid clientId);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspConnectClient(in Guid clientId, out nint client);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspDisconnectClient(nint client);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCreateProcessFilter(
        uint propertyId,
        EspComparisonType comparisonType,
        in NativeContextKeyComparison comparison,
        out nint filter);

    [DllImport(WespLibrary, EntryPoint = "EspCreateProcessFilter", ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCreateProcessStringFilter(
        uint propertyId,
        EspComparisonType comparisonType,
        in NativeStringComparison comparison,
        out nint filter);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCreateFileObjectFilter(
        uint propertyId,
        EspComparisonType comparisonType,
        in NativeStringComparison comparison,
        out nint filter);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCreateRegistryKeyFilter(
        uint propertyId,
        EspComparisonType comparisonType,
        in NativeStringComparison comparison,
        out nint filter);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCreateFilter(
        EspComparisonType comparisonType,
        in NativeIntegerComparison comparison,
        out nint filter);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCloseFilter(nint filter);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCreateRule(in NativeRuleDescriptor descriptor, out nint rule);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCloseRule(nint rule);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspUpdateRules(
        nint client,
        uint flags,
        uint entriesCount,
        [In] NativeRuleUpdateEntry[] entries);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspRemoveRulesForClient(nint client, EspRuleLifetime lifetime);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspRemoveAllRulesForClient(nint client);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCreateProcessReference(nint client, uint processId, out nint objectReference);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspGetEventObjectFromReference(nint objectReference, out nint eventObject);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCloseEventObjectReference(nint objectReference);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspSetEventObjectContextKey(nint eventObject, in NativeContextKeyUpdate update);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern unsafe int EspQueryProcessProperties(
        nint process,
        uint propertyIdsCount,
        uint* propertyIds,
        out nint properties);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern void EspFreeMemory(nint buffer);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspGetEventCapabilities(
        nint client,
        EspEventType eventType,
        out EspEventCapabilities capabilities);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspIsFileObjectPropertySupported(nint client, uint propertyId, out int isSupported);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspIsRegistryKeyPropertySupported(nint client, uint propertyId, out int isSupported);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCreateEventQueue(
        nint client,
        in NativeEventQueueDescriptor descriptor,
        out nint queue);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCloseEventQueue(nint queue);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspConnectEventQueueWithCallback(
        nint queue,
        uint maxWorkerThreadCount,
        NativeEventQueueNotificationCallback callback,
        nint callbackContext);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspDisconnectEventQueue(nint queue);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern nint EspAllocateEventNotification();

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern void EspFreeEventNotification(nint notification);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspArmEventNotification(nint queue, nint notification);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspCompleteEventNotification(nint notification);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspSetEventQueueStateChangeCallback(
        nint queue,
        NativeEventQueueStateChangeCallback callback,
        nint context,
        byte memoryThresholdPercent,
        byte recoveryThresholdPercent);

    [DllImport(WespLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    internal static extern int EspRemoveEventQueueStateChangeCallback(nint queue);

    [DllImport(Kernel32, EntryPoint = "CreateProcessW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern unsafe bool CreateProcess(
        string applicationName,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        ProcessCreationFlags creationFlags,
        nint environment,
        string currentDirectory,
        ref NativeStartupInfo startupInfo,
        out NativeProcessInformation processInformation);

    [DllImport(Kernel32, SetLastError = true)]
    internal static extern uint ResumeThread(SafeThreadHandle thread);

    [DllImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport(Kernel32, SetLastError = true)]
    internal static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);

    [DllImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessTimes(
        SafeProcessHandle process,
        out NativeFileTime creationTime,
        out NativeFileTime exitTime,
        out NativeFileTime kernelTime,
        out NativeFileTime userTime);

    [DllImport(Kernel32, SetLastError = true)]
    internal static extern nint OpenProcess(
        ProcessAccessRights desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport(Kernel32)]
    internal static extern nint GetCurrentProcess();

    [DllImport(Advapi32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(
        nint process,
        uint desiredAccess,
        out nint token);

    [DllImport(Advapi32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(
        nint token,
        NativeTokenInformationClass tokenInformationClass,
        nint tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport(Advapi32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsValidSid(nint sid);

    [DllImport(Advapi32)]
    internal static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport(Advapi32)]
    internal static extern nint GetSidSubAuthority(nint sid, uint subAuthority);

    [DllImport(Advapi32, EntryPoint = "OpenSCManagerW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeServiceHandle OpenServiceControlManager(
        string? machineName,
        string? databaseName,
        uint desiredAccess);

    [DllImport(Advapi32, EntryPoint = "OpenServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeServiceHandle OpenService(
        SafeServiceHandle serviceControlManager,
        string serviceName,
        uint desiredAccess);

    [DllImport(Advapi32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryServiceStatus(
        SafeServiceHandle service,
        out NativeServiceStatus status);

    [DllImport(Advapi32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(nint serviceHandle);
}
