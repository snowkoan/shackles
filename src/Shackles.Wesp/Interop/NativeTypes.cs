using System.Runtime.InteropServices;

namespace Shackles.Wesp.Interop;

internal enum NativeTokenInformationClass
{
    TokenIntegrityLevel = 25
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSidAndAttributes
{
    internal nint Sid;
    internal uint Attributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTokenMandatoryLabel
{
    internal NativeSidAndAttributes Label;
}

internal enum EspComparisonType
{
    None = 0,
    ContextKey = 1,
    Integer = 3,
    String = 4
}

internal enum EspIntegerComparisonType
{
    Equals = 1,
    IsAnyFlagSet = 9
}

internal enum EspStringComparisonType
{
    Equals = 1,
    PatternMatch = 3
}

internal enum EspValueSourceType
{
    Raw = 1
}

internal enum EspContextKeyComparisonType
{
    Integer = 3
}

internal enum EspContextKeyLifetime
{
    ClientSession = 1
}

internal enum EspContextKeyUpdateType
{
    CreateOrReplace = 2
}

internal enum EspContextKeyValueType
{
    Integer = 1
}

internal enum EspRuleLifetime
{
    ClientSession = 1
}

internal enum EspRuleAction
{
    Notify = 1,
    NoNotify = 3,
    Block = 5,
    ContinueMatchingNextRule = 8
}

internal enum EspBlockReason
{
    AccessDenied = 2
}

internal enum EspRuleUpdateType
{
    Add = 1
}

[Flags]
internal enum EspEventCapabilities
{
    None = 0,
    Monitor = 0x1,
    Block = 0x2
}

internal enum EspEventType
{
    ProcessCreate = 1000,
    FileObjectCreate = 2000,
    FileObjectOpen = 2001,
    FileObjectRead = 2002,
    FileObjectWrite = 2003,
    FileSystemCreateFileSection = 3000,
    FileSystemQueryFileInformation = 3001,
    FileSystemSetFileInformation = 3002,
    FileSystemSetFileSecurity = 3003,
    FileSystemQueryDirectoryInformation = 3004,
    FileSystemControlFile = 3005,
    FileSystemSetExtendedAttributes = 3006,
    FileSystemLockFile = 3008,
    RegistryCreateKey = 7000,
    RegistryOpenKey = 7001,
    RegistryDeleteKey = 7002,
    RegistrySetValue = 7003,
    RegistryDeleteValue = 7004,
    RegistryRenameKey = 7005,
    RegistryReplaceKey = 7006,
    RegistryRestoreKey = 7007,
    RegistrySetKeySecurity = 7008,
    RegistryQueryKey = 7009,
    RegistryQueryValue = 7010,
    RegistrySaveKey = 7011,
    RegistryLoadKey = 7012,
    RegistryEnumerateKey = 7013,
    RegistryEnumerateValue = 7014
}

internal enum EspEventQueueType
{
    Async = 1
}

internal enum EspEventQueueLifetime
{
    ClientSession = 1
}

internal enum EspEventNotificationVersion
{
    Version1 = 1
}

[Flags]
internal enum EspEventNotificationFlags
{
    None = 0,
    Disconnected = 0x1
}

internal enum EspEventQueueState
{
    None = 0,
    Full = 1,
    MemoryThresholdExceeded = 2,
    Recovered = 3
}

internal enum EspVariantType
{
    Error = 0,
    UInt32 = 5,
    UnicodeString = 8,
    FileTime = 10
}

[Flags]
internal enum ProcessAccessRights : uint
{
    QueryLimitedInformation = 0x00001000,
    Synchronize = 0x00100000
}

[Flags]
internal enum ProcessCreationFlags : uint
{
    Suspended = 0x00000004
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeClientDescriptor
{
    internal Guid ClientId;
    internal nint ClientName;
    internal nint Altitude;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeUnicodeString
{
    internal ushort LengthBytes;
    internal nint Buffer;
}

// ESP_PROPERTY and ESP_PROPERTY_QUERY on the 64-bit WESP ABI. A Unicode
// string property stores a pointer to ESP_UNICODE_STRING in Value.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.PropertySize)]
internal struct NativeProperty
{
    [FieldOffset(0)] internal uint PropertyId;
    [FieldOffset(4)] internal EspVariantType Type;
    [FieldOffset(8)] internal nint Value;
}

[StructLayout(LayoutKind.Sequential, Size = WespAbiV013.PropertyQuerySize)]
internal struct NativePropertyQuery
{
    internal uint PropertiesCount;
    private uint _padding;
    internal nint Properties;
}

// ESP_STRING_COMPARISON on the 64-bit WESP ABI. The nested union is flattened.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.StringComparisonSize)]
internal struct NativeStringComparison
{
    [FieldOffset(0)] internal EspStringComparisonType ComparisonType;
    [FieldOffset(8)] internal EspValueSourceType SourceType;
    [FieldOffset(12)] internal int CaseSensitive;
    [FieldOffset(16)] internal ushort StringLengthBytes;
    [FieldOffset(24)] internal nint StringBuffer;
}

// ESP_INTEGER_COMPARISON on the 64-bit WESP ABI. Unused transforms remain zero.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.IntegerComparisonSize)]
internal struct NativeIntegerComparison
{
    [FieldOffset(0)] internal EspIntegerComparisonType ComparisonType;
    [FieldOffset(24)] internal EspValueSourceType SourceType;
    [FieldOffset(48)] internal ulong RawValue;
}

// ESP_CONTEXT_KEY_COMPARISON containing an ESP_INTEGER_COMPARISON.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.ContextKeyComparisonSize)]
internal struct NativeContextKeyComparison
{
    [FieldOffset(0)] internal EspContextKeyComparisonType ComparisonType;
    [FieldOffset(8)] internal EspIntegerComparisonType IntegerComparisonType;
    [FieldOffset(32)] internal EspValueSourceType SourceType;
    [FieldOffset(56)] internal ulong RawValue;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.ContextKeyUpdateSize)]
internal struct NativeContextKeyUpdate
{
    [FieldOffset(0)] internal uint ContextKey;
    [FieldOffset(4)] internal EspContextKeyLifetime Lifetime;
    [FieldOffset(8)] internal EspContextKeyUpdateType UpdateType;
    [FieldOffset(20)] internal EspContextKeyValueType ValueType;
    [FieldOffset(24)] internal ulong IntegerValue;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeContextKeyConfig
{
    internal uint UpdatesCount;
    private uint _padding;
    internal nint Updates;
}

// Only fields used by this POC are surfaced. Size and offsets match
// ESP_PROCESS_CREATE_CONFIG; the remaining native fields stay zero.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.ProcessCreateConfigSize)]
internal struct NativeProcessCreateConfig
{
    [FieldOffset(WespAbiV013.ProcessCreateNewProcessFilter)] internal nint NewProcessFilter;
    [FieldOffset(WespAbiV013.ProcessCreateNewProcessContext)] internal NativeContextKeyConfig NewProcessContext;
    [FieldOffset(WespAbiV013.ProcessCreateNewProcessIncludeInNotification)] internal int NewProcessIncludeInNotification;
    [FieldOffset(WespAbiV013.ProcessCreateNewProcessPropertiesCount)] internal uint NewProcessPropertiesCount;
    [FieldOffset(WespAbiV013.ProcessCreateNewProcessProperties)] internal nint NewProcessProperties;
    [FieldOffset(WespAbiV013.ProcessCreateImageFileObjectFilter)] internal nint ImageFileObjectFilter;
    [FieldOffset(WespAbiV013.ProcessCreateImageFileObjectType)] internal int ImageFileObjectType;
    [FieldOffset(WespAbiV013.ProcessCreateCreatingThreadOwningProcessFilter)]
    internal nint CreatingThreadOwningProcessFilter;
}

// Every filesystem event used here begins with ESP_IO_OPERATION_CONFIG.
// This scratch representation is sized for the largest event config used by
// the POC; optional event arguments begin at byte 1384.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.IoEventConfigCapacity)]
internal struct NativeIoEventConfig
{
    [FieldOffset(WespAbiV013.IoRequestorProcessFilter)] internal nint RequestorProcessFilter;
    [FieldOffset(WespAbiV013.IoRequestorProcessIncludeInNotification)] internal int RequestorProcessIncludeInNotification;
    [FieldOffset(WespAbiV013.IoRequestorProcessPropertiesCount)] internal uint RequestorProcessPropertiesCount;
    [FieldOffset(WespAbiV013.IoRequestorProcessProperties)] internal nint RequestorProcessProperties;
    [FieldOffset(WespAbiV013.IoTargetFileObjectFilter)] internal nint TargetFileObjectFilter;
    [FieldOffset(WespAbiV013.IoTargetFileObjectIncludeInNotification)] internal int TargetFileObjectIncludeInNotification;
    [FieldOffset(WespAbiV013.IoTargetFileObjectPropertiesCount)] internal uint TargetFileObjectPropertiesCount;
    [FieldOffset(WespAbiV013.IoTargetFileObjectProperties)] internal nint TargetFileObjectProperties;
    [FieldOffset(WespAbiV013.IoTargetFileObjectType)] internal int TargetFileObjectType;
    [FieldOffset(WespAbiV013.IoFirstArgumentFilter)] internal nint FirstArgumentFilter;
    [FieldOffset(WespAbiV013.IoSecondArgumentFilter)] internal nint SecondArgumentFilter;
    [FieldOffset(WespAbiV013.IoThirdArgumentFilter)] internal nint ThirdArgumentFilter;
    [FieldOffset(WespAbiV013.IoParentTargetFileObjectFilter)] internal nint ParentTargetFileObjectFilter;
    [FieldOffset(WespAbiV013.IoParentTargetFileObjectType)] internal int ParentTargetFileObjectType;
}

// Scratch representation for every registry event config used by this POC.
// The event-specific native structures are smaller; WESP reads the shape that
// corresponds to EventType. Only common nested path and access filters surface.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.RegistryEventConfigCapacity)]
internal struct NativeRegistryEventConfig
{
    [FieldOffset(WespAbiV013.RegistryKeyFilter)] internal nint RegistryKeyFilter;
    [FieldOffset(WespAbiV013.RegistryKeyIncludeInNotification)] internal int RegistryKeyIncludeInNotification;
    [FieldOffset(WespAbiV013.RegistryKeyPropertiesCount)] internal uint RegistryKeyPropertiesCount;
    [FieldOffset(WespAbiV013.RegistryKeyProperties)] internal nint RegistryKeyProperties;
    [FieldOffset(WespAbiV013.RegistryKeyObjectKeyFilter)] internal nint RegistryKeyObjectKeyFilter;
    [FieldOffset(WespAbiV013.RegistryKeyObjectKeyIncludeInNotification)] internal int RegistryKeyObjectKeyIncludeInNotification;
    [FieldOffset(WespAbiV013.RegistryKeyObjectKeyPropertiesCount)] internal uint RegistryKeyObjectKeyPropertiesCount;
    [FieldOffset(WespAbiV013.RegistryKeyObjectKeyProperties)] internal nint RegistryKeyObjectKeyProperties;
    [FieldOffset(WespAbiV013.RegistryCreateDesiredAccessFilter)] internal nint CreateDesiredAccessFilter;
    [FieldOffset(WespAbiV013.RegistryOpenDesiredAccessFilter)] internal nint OpenDesiredAccessFilter;
}

// ESP_RULE_DESCRIPTOR embeds a 1048-byte ESP_RULE_CONFIG. Flattening the few
// fields we use avoids treating WESP's large nested unions as managed objects.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.RuleDescriptorSize)]
internal struct NativeRuleDescriptor
{
    [FieldOffset(0)] internal Guid RuleId;
    [FieldOffset(WespAbiV013.RuleOrderGroup)] internal ulong OrderGroup;
    [FieldOffset(WespAbiV013.RuleLifetime)] internal EspRuleLifetime Lifetime;
    [FieldOffset(WespAbiV013.RuleFlags)] internal uint RuleFlags;
    [FieldOffset(WespAbiV013.RuleEventType)] internal EspEventType EventType;
    [FieldOffset(WespAbiV013.RuleEventTypeConfig)] internal nint EventTypeConfig;
    [FieldOffset(WespAbiV013.RuleCurrentProcessFilter)] internal nint CurrentProcessFilter;
    [FieldOffset(WespAbiV013.RuleCurrentProcessIncludeInNotification)] internal int CurrentProcessIncludeInNotification;
    [FieldOffset(WespAbiV013.RuleCurrentProcessPropertiesCount)] internal uint CurrentProcessPropertiesCount;
    [FieldOffset(WespAbiV013.RuleCurrentProcessProperties)] internal nint CurrentProcessProperties;
    [FieldOffset(WespAbiV013.RuleAction)] internal EspRuleAction Action;
    [FieldOffset(WespAbiV013.RuleBlockReason)] internal EspBlockReason BlockReason;
    [FieldOffset(WespAbiV013.RuleNotifyAsyncEventQueue)] internal nint NotifyAsyncEventQueue;
    [FieldOffset(WespAbiV013.RuleBlockAsyncEventQueue)] internal nint BlockAsyncEventQueue;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.EventQueueDescriptorSize)]
internal struct NativeEventQueueDescriptor
{
    [FieldOffset(0)] internal Guid QueueId;
    [FieldOffset(16)] internal EspEventQueueType QueueType;
    [FieldOffset(20)] internal EspEventQueueLifetime Lifetime;
    [FieldOffset(24)] internal uint MaxCapacityBytes;
    [FieldOffset(28)] internal EspEventNotificationVersion NotificationVersion;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.EventNotificationSize)]
internal struct NativeEventNotification
{
    [FieldOffset(0)] internal Guid EventQueueId;
    [FieldOffset(WespAbiV013.EventNotificationVersion)] internal EspEventNotificationVersion Version;
    [FieldOffset(WespAbiV013.EventNotificationData)] internal nint NotificationData;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.EventNotificationDataV1HeaderSize)]
internal struct NativeEventNotificationDataV1Header
{
    [FieldOffset(0)] internal ulong EventId;
    [FieldOffset(WespAbiV013.EventNotificationDataV1RuleId)] internal Guid RuleId;
    [FieldOffset(WespAbiV013.EventNotificationDataV1RuleAction)] internal EspRuleAction RuleAction;
    [FieldOffset(WespAbiV013.EventNotificationDataV1Flags)] internal EspEventNotificationFlags NotificationFlags;
}

// Only fields consumed by the activity queue are surfaced. The offsets are
// from ESP_EVENT_NOTIFICATION_DATA_V1 in the preview 0.13 ABI.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.EventNotificationDataV1Size)]
internal struct NativeEventNotificationDataV1
{
    [FieldOffset(0)] internal ulong EventId;
    [FieldOffset(WespAbiV013.EventNotificationDataV1RuleId)] internal Guid RuleId;
    [FieldOffset(WespAbiV013.EventNotificationDataV1RuleAction)] internal EspRuleAction RuleAction;
    [FieldOffset(WespAbiV013.EventNotificationDataV1Flags)] internal EspEventNotificationFlags NotificationFlags;
    [FieldOffset(WespAbiV013.EventNotificationDataV1CurrentProcessQuery)] internal NativePropertyQuery CurrentProcessQuery;
    [FieldOffset(WespAbiV013.EventNotificationDataV1EventType)] internal EspEventType EventType;
    [FieldOffset(WespAbiV013.EventNotificationDataV1EventParameters)] internal nint EventParameters;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.ProcessInfoIdentitySize)]
internal struct NativeProcessInfoIdentity
{
    [FieldOffset(WespAbiV013.ProcessInfoPropertyQuery)] internal NativePropertyQuery ProcessQuery;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.IoOperationIdentitySize)]
internal struct NativeIoOperationIdentity
{
    [FieldOffset(WespAbiV013.IoOperationRequestorProcess)] internal nint RequestorProcess;
    [FieldOffset(WespAbiV013.IoOperationTargetFileObject)] internal nint TargetFileObject;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.EventObjectInfoIdentitySize)]
internal struct NativeEventObjectInfoIdentity
{
    [FieldOffset(WespAbiV013.EventObjectInfoPropertyQuery)] internal NativePropertyQuery PropertyQuery;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.RegistryKeyObjectInfoIdentitySize)]
internal struct NativeRegistryKeyObjectInfoIdentity
{
    [FieldOffset(WespAbiV013.RegistryKeyObjectInfoRegistryKeyInfo)] internal nint RegistryKeyInfo;
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void NativeEventQueueNotificationCallback(nint notification, nint context);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void NativeEventQueueStateChangeCallback(
    EspEventQueueState state,
    nint data,
    nint context);

[StructLayout(LayoutKind.Explicit, Size = WespAbiV013.RuleUpdateEntrySize)]
internal struct NativeRuleUpdateEntry
{
    [FieldOffset(0)] internal EspRuleUpdateType UpdateType;
    [FieldOffset(8)] internal nint Rule;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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
internal struct NativeServiceStatus
{
    internal uint ServiceType;
    internal uint CurrentState;
    internal uint ControlsAccepted;
    internal uint Win32ExitCode;
    internal uint ServiceSpecificExitCode;
    internal uint CheckPoint;
    internal uint WaitHint;
}
