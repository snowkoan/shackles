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
[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.PropertySize)]
internal struct NativeProperty
{
    [FieldOffset(0)] internal uint PropertyId;
    [FieldOffset(4)] internal EspVariantType Type;
    [FieldOffset(8)] internal nint Value;
}

[StructLayout(LayoutKind.Sequential, Size = WespAbiV100.PropertyQuerySize)]
internal struct NativePropertyQuery
{
    internal uint PropertiesCount;
    private uint _padding;
    internal nint Properties;
}

// ESP_STRING_COMPARISON on the 64-bit WESP ABI. The nested union is flattened.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.StringComparisonSize)]
internal struct NativeStringComparison
{
    [FieldOffset(0)] internal EspStringComparisonType ComparisonType;
    [FieldOffset(8)] internal EspValueSourceType SourceType;
    [FieldOffset(12)] internal int CaseSensitive;
    [FieldOffset(16)] internal ushort StringLengthBytes;
    [FieldOffset(24)] internal nint StringBuffer;
}

// ESP_INTEGER_COMPARISON on the 64-bit WESP ABI. Unused transforms remain zero.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.IntegerComparisonSize)]
internal struct NativeIntegerComparison
{
    [FieldOffset(0)] internal EspIntegerComparisonType ComparisonType;
    [FieldOffset(24)] internal EspValueSourceType SourceType;
    [FieldOffset(48)] internal ulong RawValue;
}

// ESP_CONTEXT_KEY_COMPARISON containing an ESP_INTEGER_COMPARISON.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.ContextKeyComparisonSize)]
internal struct NativeContextKeyComparison
{
    [FieldOffset(0)] internal EspContextKeyComparisonType ComparisonType;
    [FieldOffset(8)] internal EspIntegerComparisonType IntegerComparisonType;
    [FieldOffset(32)] internal EspValueSourceType SourceType;
    [FieldOffset(56)] internal ulong RawValue;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.ContextKeyUpdateSize)]
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
[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.ProcessCreateConfigSize)]
internal struct NativeProcessCreateConfig
{
    [FieldOffset(WespAbiV100.ProcessCreateNewProcessFilter)] internal nint NewProcessFilter;
    [FieldOffset(WespAbiV100.ProcessCreateNewProcessContext)] internal NativeContextKeyConfig NewProcessContext;
    [FieldOffset(WespAbiV100.ProcessCreateNewProcessIncludeInNotification)] internal int NewProcessIncludeInNotification;
    [FieldOffset(WespAbiV100.ProcessCreateNewProcessPropertiesCount)] internal uint NewProcessPropertiesCount;
    [FieldOffset(WespAbiV100.ProcessCreateNewProcessProperties)] internal nint NewProcessProperties;
    [FieldOffset(WespAbiV100.ProcessCreateImageFileObjectFilter)] internal nint ImageFileObjectFilter;
    [FieldOffset(WespAbiV100.ProcessCreateImageFileObjectType)] internal int ImageFileObjectType;
    [FieldOffset(WespAbiV100.ProcessCreateCreatingThreadOwningProcessFilter)]
    internal nint CreatingThreadOwningProcessFilter;
}

// Every filesystem event used here begins with ESP_IO_OPERATION_CONFIG.
// This scratch representation is sized for the largest event config used by
// the POC; optional event arguments begin at byte 1384.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.IoEventConfigCapacity)]
internal struct NativeIoEventConfig
{
    [FieldOffset(WespAbiV100.IoRequestorProcessFilter)] internal nint RequestorProcessFilter;
    [FieldOffset(WespAbiV100.IoRequestorProcessIncludeInNotification)] internal int RequestorProcessIncludeInNotification;
    [FieldOffset(WespAbiV100.IoRequestorProcessPropertiesCount)] internal uint RequestorProcessPropertiesCount;
    [FieldOffset(WespAbiV100.IoRequestorProcessProperties)] internal nint RequestorProcessProperties;
    [FieldOffset(WespAbiV100.IoTargetFileObjectFilter)] internal nint TargetFileObjectFilter;
    [FieldOffset(WespAbiV100.IoTargetFileObjectIncludeInNotification)] internal int TargetFileObjectIncludeInNotification;
    [FieldOffset(WespAbiV100.IoTargetFileObjectPropertiesCount)] internal uint TargetFileObjectPropertiesCount;
    [FieldOffset(WespAbiV100.IoTargetFileObjectProperties)] internal nint TargetFileObjectProperties;
    [FieldOffset(WespAbiV100.IoTargetFileObjectType)] internal int TargetFileObjectType;
    [FieldOffset(WespAbiV100.IoFirstArgumentFilter)] internal nint FirstArgumentFilter;
    [FieldOffset(WespAbiV100.IoSecondArgumentFilter)] internal nint SecondArgumentFilter;
    [FieldOffset(WespAbiV100.IoThirdArgumentFilter)] internal nint ThirdArgumentFilter;
    [FieldOffset(WespAbiV100.IoParentTargetFileObjectFilter)] internal nint ParentTargetFileObjectFilter;
    [FieldOffset(WespAbiV100.IoParentTargetFileObjectType)] internal int ParentTargetFileObjectType;
}

// Scratch representation for every registry event config used by this POC.
// The event-specific native structures are smaller; WESP reads the shape that
// corresponds to EventType. Only common nested path and access filters surface.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.RegistryEventConfigCapacity)]
internal struct NativeRegistryEventConfig
{
    [FieldOffset(WespAbiV100.RegistryKeyFilter)] internal nint RegistryKeyFilter;
    [FieldOffset(WespAbiV100.RegistryKeyIncludeInNotification)] internal int RegistryKeyIncludeInNotification;
    [FieldOffset(WespAbiV100.RegistryKeyPropertiesCount)] internal uint RegistryKeyPropertiesCount;
    [FieldOffset(WespAbiV100.RegistryKeyProperties)] internal nint RegistryKeyProperties;
    [FieldOffset(WespAbiV100.RegistryKeyObjectKeyFilter)] internal nint RegistryKeyObjectKeyFilter;
    [FieldOffset(WespAbiV100.RegistryKeyObjectKeyIncludeInNotification)] internal int RegistryKeyObjectKeyIncludeInNotification;
    [FieldOffset(WespAbiV100.RegistryKeyObjectKeyPropertiesCount)] internal uint RegistryKeyObjectKeyPropertiesCount;
    [FieldOffset(WespAbiV100.RegistryKeyObjectKeyProperties)] internal nint RegistryKeyObjectKeyProperties;
    [FieldOffset(WespAbiV100.RegistryCreateDesiredAccessFilter)] internal nint CreateDesiredAccessFilter;
    [FieldOffset(WespAbiV100.RegistryOpenDesiredAccessFilter)] internal nint OpenDesiredAccessFilter;
}

// ESP_RULE_DESCRIPTOR embeds a 1048-byte ESP_RULE_CONFIG. Flattening the few
// fields we use avoids treating WESP's large nested unions as managed objects.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.RuleDescriptorSize)]
internal struct NativeRuleDescriptor
{
    [FieldOffset(0)] internal Guid RuleId;
    [FieldOffset(WespAbiV100.RuleOrderGroup)] internal ulong OrderGroup;
    [FieldOffset(WespAbiV100.RuleLifetime)] internal EspRuleLifetime Lifetime;
    [FieldOffset(WespAbiV100.RuleFlags)] internal uint RuleFlags;
    [FieldOffset(WespAbiV100.RuleEventType)] internal EspEventType EventType;
    [FieldOffset(WespAbiV100.RuleEventTypeConfig)] internal nint EventTypeConfig;
    [FieldOffset(WespAbiV100.RuleCurrentProcessFilter)] internal nint CurrentProcessFilter;
    [FieldOffset(WespAbiV100.RuleCurrentProcessIncludeInNotification)] internal int CurrentProcessIncludeInNotification;
    [FieldOffset(WespAbiV100.RuleCurrentProcessPropertiesCount)] internal uint CurrentProcessPropertiesCount;
    [FieldOffset(WespAbiV100.RuleCurrentProcessProperties)] internal nint CurrentProcessProperties;
    [FieldOffset(WespAbiV100.RuleAction)] internal EspRuleAction Action;
    [FieldOffset(WespAbiV100.RuleBlockReason)] internal EspBlockReason BlockReason;
    [FieldOffset(WespAbiV100.RuleNotifyAsyncEventQueue)] internal nint NotifyAsyncEventQueue;
    [FieldOffset(WespAbiV100.RuleBlockAsyncEventQueue)] internal nint BlockAsyncEventQueue;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.EventQueueDescriptorSize)]
internal struct NativeEventQueueDescriptor
{
    [FieldOffset(0)] internal Guid QueueId;
    [FieldOffset(16)] internal EspEventQueueType QueueType;
    [FieldOffset(20)] internal EspEventQueueLifetime Lifetime;
    [FieldOffset(24)] internal uint MaxCapacityBytes;
    [FieldOffset(28)] internal EspEventNotificationVersion NotificationVersion;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.EventNotificationSize)]
internal struct NativeEventNotification
{
    [FieldOffset(0)] internal Guid EventQueueId;
    [FieldOffset(WespAbiV100.EventNotificationVersion)] internal EspEventNotificationVersion Version;
    [FieldOffset(WespAbiV100.EventNotificationData)] internal nint NotificationData;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.EventNotificationDataV1HeaderSize)]
internal struct NativeEventNotificationDataV1Header
{
    [FieldOffset(0)] internal ulong EventId;
    [FieldOffset(WespAbiV100.EventNotificationDataV1RuleId)] internal Guid RuleId;
    [FieldOffset(WespAbiV100.EventNotificationDataV1RuleAction)] internal EspRuleAction RuleAction;
    [FieldOffset(WespAbiV100.EventNotificationDataV1Flags)] internal EspEventNotificationFlags NotificationFlags;
}

// Only fields consumed by the activity queue are surfaced. The offsets are
// from ESP_EVENT_NOTIFICATION_DATA_V1 in the preview 1.0 ABI.
[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.EventNotificationDataV1Size)]
internal struct NativeEventNotificationDataV1
{
    [FieldOffset(0)] internal ulong EventId;
    [FieldOffset(WespAbiV100.EventNotificationDataV1RuleId)] internal Guid RuleId;
    [FieldOffset(WespAbiV100.EventNotificationDataV1RuleAction)] internal EspRuleAction RuleAction;
    [FieldOffset(WespAbiV100.EventNotificationDataV1Flags)] internal EspEventNotificationFlags NotificationFlags;
    [FieldOffset(WespAbiV100.EventNotificationDataV1CurrentProcessQuery)] internal NativePropertyQuery CurrentProcessQuery;
    [FieldOffset(WespAbiV100.EventNotificationDataV1EventType)] internal EspEventType EventType;
    [FieldOffset(WespAbiV100.EventNotificationDataV1EventParameters)] internal nint EventParameters;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.ProcessInfoIdentitySize)]
internal struct NativeProcessInfoIdentity
{
    [FieldOffset(WespAbiV100.ProcessInfoPropertyQuery)] internal NativePropertyQuery ProcessQuery;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.IoOperationIdentitySize)]
internal struct NativeIoOperationIdentity
{
    [FieldOffset(WespAbiV100.IoOperationRequestorProcess)] internal nint RequestorProcess;
    [FieldOffset(WespAbiV100.IoOperationTargetFileObject)] internal nint TargetFileObject;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.EventObjectInfoIdentitySize)]
internal struct NativeEventObjectInfoIdentity
{
    [FieldOffset(WespAbiV100.EventObjectInfoPropertyQuery)] internal NativePropertyQuery PropertyQuery;
}

[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.RegistryKeyObjectInfoIdentitySize)]
internal struct NativeRegistryKeyObjectInfoIdentity
{
    [FieldOffset(WespAbiV100.RegistryKeyObjectInfoRegistryKeyInfo)] internal nint RegistryKeyInfo;
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void NativeEventQueueNotificationCallback(nint notification, nint context);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void NativeEventQueueStateChangeCallback(
    EspEventQueueState state,
    nint data,
    nint context);

[StructLayout(LayoutKind.Explicit, Size = WespAbiV100.RuleUpdateEntrySize)]
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
