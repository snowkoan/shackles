using System.Runtime.InteropServices;

namespace Shackles.JobObjects.Interop;

internal enum JobObjectInformationClass
{
    BasicAccountingInformation = 1,
    BasicProcessIdList = 3,
    BasicUiRestrictions = 4,
    EndOfJobTimeInformation = 6,
    AssociateCompletionPortInformation = 7,
    BasicAndIoAccountingInformation = 8,
    ExtendedLimitInformation = 9,
    GroupInformationEx = 14,
    CpuRateControlInformation = 15,
    NetRateControlInformation = 32,
    NotificationLimitInformation2 = 33,
    LimitViolationInformation2 = 34
}

[Flags]
internal enum NativeExtendedLimitFlags : uint
{
    WorkingSet = 0x00000001,
    ProcessTime = 0x00000002,
    JobTime = 0x00000004,
    ActiveProcess = 0x00000008,
    Affinity = 0x00000010,
    PriorityClass = 0x00000020,
    PreserveJobTime = 0x00000040,
    SchedulingClass = 0x00000080,
    ProcessMemory = 0x00000100,
    JobMemory = 0x00000200,
    DieOnUnhandledException = 0x00000400,
    BreakawayOk = 0x00000800,
    SilentBreakawayOk = 0x00001000,
    KillOnJobClose = 0x00002000,
    SubsetAffinity = 0x00004000
}

[Flags]
internal enum NativeCpuRateFlags : uint
{
    Enable = 0x00000001,
    WeightBased = 0x00000002,
    HardCap = 0x00000004,
    Notify = 0x00000008,
    MinMaxRate = 0x00000010,
    PerProcessorCaps = 0x00000020
}

[Flags]
internal enum NativeNetworkRateFlags : uint
{
    Enable = 0x00000001,
    MaximumBandwidth = 0x00000002,
    DscpTag = 0x00000004
}

[Flags]
internal enum ProcessAccessRights : uint
{
    Terminate = 0x00000001,
    SetQuota = 0x00000100,
    QueryLimitedInformation = 0x00001000
}

[Flags]
internal enum ProcessCreationFlags : uint
{
    Suspended = 0x00000004,
    CreateNoWindow = 0x08000000
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeIoCounters
{
    internal ulong ReadOperationCount;
    internal ulong WriteOperationCount;
    internal ulong OtherOperationCount;
    internal ulong ReadTransferCount;
    internal ulong WriteTransferCount;
    internal ulong OtherTransferCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeBasicAccountingInformation
{
    internal long TotalUserTime;
    internal long TotalKernelTime;
    internal long ThisPeriodTotalUserTime;
    internal long ThisPeriodTotalKernelTime;
    internal uint TotalPageFaultCount;
    internal uint TotalProcesses;
    internal uint ActiveProcesses;
    internal uint TotalTerminatedProcesses;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeBasicAndIoAccountingInformation
{
    internal NativeBasicAccountingInformation BasicInfo;
    internal NativeIoCounters IoInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeBasicLimitInformation
{
    internal long PerProcessUserTimeLimit;
    internal long PerJobUserTimeLimit;
    internal NativeExtendedLimitFlags LimitFlags;
    internal nuint MinimumWorkingSetSize;
    internal nuint MaximumWorkingSetSize;
    internal uint ActiveProcessLimit;
    internal nuint Affinity;
    internal uint PriorityClass;
    internal uint SchedulingClass;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeExtendedLimitInformation
{
    internal NativeBasicLimitInformation BasicLimitInformation;
    internal NativeIoCounters IoInfo;
    internal nuint ProcessMemoryLimit;
    internal nuint JobMemoryLimit;
    internal nuint PeakProcessMemoryUsed;
    internal nuint PeakJobMemoryUsed;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeBasicUiRestrictions
{
    internal JobUiRestrictions UiRestrictionsClass;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEndOfJobTimeInformation
{
    internal JobEndOfJobAction EndOfJobTimeAction;
}

[StructLayout(LayoutKind.Explicit)]
internal struct NativeCpuRateValue
{
    [FieldOffset(0)]
    internal uint CpuRate;

    [FieldOffset(0)]
    internal uint Weight;

    [FieldOffset(0)]
    internal ushort MinimumRate;

    [FieldOffset(2)]
    internal ushort MaximumRate;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCpuRateControlInformation
{
    internal NativeCpuRateFlags ControlFlags;
    internal NativeCpuRateValue Value;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeNetworkRateControlInformation
{
    internal ulong MaximumBandwidth;
    internal NativeNetworkRateFlags ControlFlags;
    internal byte DscpTag;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeNotificationLimitInformation2
{
    internal ulong IoReadBytesLimit;
    internal ulong IoWriteBytesLimit;
    internal long PerJobUserTimeLimit;
    internal ulong JobHighMemoryLimit;
    internal JobRateControlTolerance CpuRateControlTolerance;
    internal JobRateControlToleranceInterval CpuRateControlToleranceInterval;
    internal JobNotificationLimitFlags LimitFlags;
    internal JobRateControlTolerance IoRateControlTolerance;
    internal ulong JobLowMemoryLimit;
    internal JobRateControlToleranceInterval IoRateControlToleranceInterval;
    internal JobRateControlTolerance NetRateControlTolerance;
    internal JobRateControlToleranceInterval NetRateControlToleranceInterval;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeLimitViolationInformation2
{
    internal JobNotificationLimitFlags LimitFlags;
    internal JobNotificationLimitFlags ViolationLimitFlags;
    internal ulong IoReadBytes;
    internal ulong IoReadBytesLimit;
    internal ulong IoWriteBytes;
    internal ulong IoWriteBytesLimit;
    internal long PerJobUserTime;
    internal long PerJobUserTimeLimit;
    internal ulong JobMemory;
    internal ulong JobHighMemoryLimit;
    internal JobRateControlTolerance CpuRateControlTolerance;
    internal JobRateControlTolerance CpuRateControlToleranceLimit;
    internal ulong JobLowMemoryLimit;
    internal JobRateControlTolerance IoRateControlTolerance;
    internal JobRateControlTolerance IoRateControlToleranceLimit;
    internal JobRateControlTolerance NetRateControlTolerance;
    internal JobRateControlTolerance NetRateControlToleranceLimit;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeGroupAffinity
{
    internal nuint Mask;
    internal ushort Group;
    private ushort Reserved0;
    private ushort Reserved1;
    private ushort Reserved2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFileTime
{
    internal uint LowDateTime;
    internal uint HighDateTime;

    internal readonly long ToLong() => unchecked((long)(((ulong)HighDateTime << 32) | LowDateTime));
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct NativeStartupInfo
{
    internal uint Size;
    internal char* Reserved;
    internal char* Desktop;
    internal char* Title;
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
    internal byte* Reserved2;
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
internal struct NativeAssociateCompletionPort
{
    internal nint CompletionKey;
    internal nint CompletionPort;
}
