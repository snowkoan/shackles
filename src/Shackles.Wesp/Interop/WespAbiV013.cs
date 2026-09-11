namespace Shackles.Wesp.Interop;

// ABI profile for the WESP preview 0.13.0 headers and binaries. WESP is a
// preview API: do not reuse these values for a different espclient.dll build.
internal static class WespAbiV013
{
    internal const string FileVersion = "0.1.0.154553750";
    internal const string ProductVersion = "0.1.0.154553750+3639f00d";

    internal const int StringComparisonSize = 32;
    internal const int IntegerComparisonSize = 56;
    internal const int ContextKeyComparisonSize = 64;
    internal const int ContextKeyUpdateSize = 40;
    internal const int PropertySize = 16;
    internal const int PropertyQuerySize = 16;
    internal const int RuleDescriptorSize = 1128;
    internal const int RuleUpdateEntrySize = 24;

    internal const int ProcessCreateConfigSize = 1328;
    internal const int ProcessCreateNewProcessFilter = 8;
    internal const int ProcessCreateNewProcessContext = 16;
    internal const int ProcessCreateNewProcessIncludeInNotification = 32;
    internal const int ProcessCreateNewProcessPropertiesCount = 48;
    internal const int ProcessCreateNewProcessProperties = 56;
    internal const int ProcessCreateImageFileObjectFilter = 400;
    internal const int ProcessCreateImageFileObjectType = 520;
    // PROCESS_CREATE.CreatingThreadConfig.OwningProcessConfig.EventObjectConfig.Filter.
    // The creating-thread EventObjectConfig.Filter is at 816 and is not
    // type-compatible with the process-context filter used for membership.
    internal const int ProcessCreateCreatingThreadOwningProcessFilter = 944;

    // Shared scratch capacity for the event-specific filesystem configs used by
    // this POC. ESP_FS_SET_FILE_INFORMATION_CONFIG is the largest at 1864 bytes.
    internal const int IoEventConfigCapacity = 1864;
    internal const int IoRequestorProcessFilter = 544;
    internal const int IoRequestorProcessIncludeInNotification = 568;
    internal const int IoRequestorProcessPropertiesCount = 584;
    internal const int IoRequestorProcessProperties = 592;
    internal const int IoTargetFileObjectFilter = 936;
    internal const int IoTargetFileObjectIncludeInNotification = 960;
    internal const int IoTargetFileObjectPropertiesCount = 976;
    internal const int IoTargetFileObjectProperties = 984;
    internal const int IoTargetFileObjectType = 1056;
    internal const int IoFirstArgumentFilter = 1384;
    internal const int IoSecondArgumentFilter = 1400;
    internal const int IoThirdArgumentFilter = 1416;
    internal const int IoParentTargetFileObjectFilter = 1408;
    internal const int IoParentTargetFileObjectType = 1528;

    // Registry event configs use either a prospective key (create/open) or a
    // key object whose nested RegistryKeyConfig describes the affected key.
    internal const int RegistryEventConfigCapacity = 600;
    internal const int RegistryKeyFilter = 8;
    internal const int RegistryKeyIncludeInNotification = 32;
    internal const int RegistryKeyPropertiesCount = 48;
    internal const int RegistryKeyProperties = 56;
    internal const int RegistryKeyObjectKeyFilter = 72;
    internal const int RegistryKeyObjectKeyIncludeInNotification = 96;
    internal const int RegistryKeyObjectKeyPropertiesCount = 112;
    internal const int RegistryKeyObjectKeyProperties = 120;
    internal const int RegistryCreateDesiredAccessFilter = 128;
    internal const int RegistryOpenDesiredAccessFilter = 80;

    internal const int RuleOrderGroup = 16;
    internal const int RuleLifetime = 24;
    internal const int RuleFlags = 28;
    internal const int RuleEventType = 32;
    internal const int RuleEventTypeConfig = 96;
    internal const int RuleCurrentProcessFilter = 688;
    internal const int RuleCurrentProcessIncludeInNotification = 712;
    internal const int RuleCurrentProcessPropertiesCount = 728;
    internal const int RuleCurrentProcessProperties = 736;
    internal const int RuleAction = 1104;
    internal const int RuleBlockReason = 1112;
    internal const int RuleNotifyAsyncEventQueue = 1112;
    internal const int RuleBlockAsyncEventQueue = 1120;

    internal const int EventQueueDescriptorSize = 32;
    internal const int EventNotificationSize = 32;
    internal const int EventNotificationVersion = 16;
    internal const int EventNotificationData = 24;
    internal const int EventNotificationDataV1HeaderSize = 32;
    internal const int EventNotificationDataV1RuleId = 8;
    internal const int EventNotificationDataV1RuleAction = 24;
    internal const int EventNotificationDataV1Flags = 28;
    internal const int EventNotificationDataV1Size = 168;
    // CurrentProcess.ObjectInfo.PropertyQuery.
    internal const int EventNotificationDataV1CurrentProcessQuery = 80;
    internal const int EventNotificationDataV1EventType = 120;
    internal const int EventNotificationDataV1EventParameters = 160;

    // ESP_PROCESS_INFO.ObjectInfo.PropertyQuery.
    internal const int ProcessInfoIdentitySize = 24;
    internal const int ProcessInfoPropertyQuery = 8;

    // ESP_IO_OPERATION.RequestorProcess and TargetFileObject.
    internal const int IoOperationIdentitySize = 32;
    internal const int IoOperationRequestorProcess = 16;
    internal const int IoOperationTargetFileObject = 24;

    // ESP_EVENT_OBJECT_INFO.PropertyQuery. ESP_FILE_OBJECT_INFO and
    // ESP_REGISTRY_KEY_INFO both begin with this common object-info header.
    internal const int EventObjectInfoIdentitySize = 24;
    internal const int EventObjectInfoPropertyQuery = 8;

    // ESP_REGISTRY_KEY_OBJECT_INFO.RegistryKeyInfo.
    internal const int RegistryKeyObjectInfoIdentitySize = 32;
    internal const int RegistryKeyObjectInfoRegistryKeyInfo = 24;
}
