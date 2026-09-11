using System.Security.Cryptography;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Internal;

internal enum ReadOnlyCreateRuleField
{
    DesiredAccess,
    CreateOptions,
    CreateDisposition
}

internal readonly record struct ReadOnlyCreateRuleSpecification(
    EspEventType EventType,
    ReadOnlyCreateRuleField Field,
    EspIntegerComparisonType ComparisonType,
    ulong Value);

internal static class WespRuleCompiler
{
    internal const uint PolicyContextKey = 0x0053484B;
    internal const string UncNtPathRoot = @"\Device\Mup";
    private const uint ProcessContextPropertyBase = 0x80000000;
    internal const uint FileObjectNormalizedNtPathProperty = 1;
    internal const uint FileObjectNormalizedDosPathProperty = 2;
    internal const uint FileObjectFinalComponentProperty = 6;
    internal const uint ProcessCreateTimeProperty = 2;
    internal const uint ProcessIdProperty = 6;
    internal const uint ProcessImageNtPathProperty = 20;
    internal const uint ProcessImageDosPathProperty = 21;
    internal const uint RegistryKeyNtPathProperty = 1;

    private static readonly uint[] ActivityProcessProperties =
    [
        ProcessIdProperty,
        ProcessImageNtPathProperty,
        ProcessImageDosPathProperty
    ];

    private static readonly uint[] ActivityFileTargetProperties =
    [
        FileObjectNormalizedNtPathProperty,
        FileObjectNormalizedDosPathProperty
    ];

    private static readonly uint[] ActivityRegistryTargetProperties =
    [
        RegistryKeyNtPathProperty
    ];

    private const ulong WriteAccessMask =
        0x00000002 | // FILE_WRITE_DATA / FILE_ADD_FILE
        0x00000004 | // FILE_APPEND_DATA / FILE_ADD_SUBDIRECTORY
        0x00000010 | // FILE_WRITE_EA
        0x00000040 | // FILE_DELETE_CHILD
        0x00000100 | // FILE_WRITE_ATTRIBUTES
        0x00010000 | // DELETE
        0x00040000 | // WRITE_DAC
        0x00080000 | // WRITE_OWNER
        0x01000000 | // ACCESS_SYSTEM_SECURITY
        0x02000000 | // MAXIMUM_ALLOWED
        0x10000000 | // GENERIC_ALL
        0x40000000;  // GENERIC_WRITE

    private const ulong WritablePageProtectionMask =
        0x00000004 | // PAGE_READWRITE
        0x00000008 | // PAGE_WRITECOPY
        0x00000040 | // PAGE_EXECUTE_READWRITE
        0x00000080;  // PAGE_EXECUTE_WRITECOPY

    private const ulong DeleteOnCloseCreateOption = 0x00001000;

    private const ulong RegistryWriteAccessMask =
        0x00000002 | // KEY_SET_VALUE
        0x00000004 | // KEY_CREATE_SUB_KEY
        0x00000020 | // KEY_CREATE_LINK
        0x00010000 | // DELETE
        0x00040000 | // WRITE_DAC
        0x00080000 | // WRITE_OWNER
        0x01000000 | // ACCESS_SYSTEM_SECURITY
        0x02000000 | // MAXIMUM_ALLOWED
        0x10000000 | // GENERIC_ALL
        0x40000000;  // GENERIC_WRITE

    private static readonly ulong[] MutatingCreateDispositions =
    [
        0, // FILE_SUPERSEDE
        2, // FILE_CREATE
        3, // FILE_OPEN_IF
        4, // FILE_OVERWRITE
        5  // FILE_OVERWRITE_IF
    ];

    private static readonly ulong[] RenameFileInformationClasses =
    [
        10, // FileRenameInformation
        56, // FileRenameInformationBypassAccessCheck
        65, // FileRenameInformationEx
        66  // FileRenameInformationExBypassAccessCheck
    ];

    private static readonly EspEventType[] RegistryMutationEvents =
    [
        EspEventType.RegistryDeleteKey,
        EspEventType.RegistrySetValue,
        EspEventType.RegistryDeleteValue,
        EspEventType.RegistryRenameKey,
        EspEventType.RegistryReplaceKey,
        EspEventType.RegistryRestoreKey,
        EspEventType.RegistrySetKeySecurity,
        EspEventType.RegistryLoadKey
    ];

    private static readonly EspEventType[] RegistryReadEvents =
    [
        EspEventType.RegistryQueryKey,
        EspEventType.RegistryQueryValue,
        EspEventType.RegistrySaveKey,
        EspEventType.RegistryEnumerateKey,
        EspEventType.RegistryEnumerateValue
    ];

    internal static ulong CreatePolicyId()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt64(bytes);
        return value == 0 ? 1UL : value;
    }

    internal static void Install(
        nint client,
        NormalizedWespPolicy policy,
        ulong policyId,
        WespEventQueue eventQueue)
    {
        var filters = new List<nint>();
        var rules = new List<nint>();
        try
        {
            var memberFilter = AddFilter(filters, CreateContextFilter(policyId));
            const ulong blockedImageOrder = 200;
            foreach (var imageName in GetBlockedChildImageNames(policy))
            {
                var imageFilter = AddFilter(
                    filters,
                    CreateFileObjectStringFilter(
                        FileObjectFinalComponentProperty,
                        imageName,
                        pattern: false,
                        "application-name"));
                AddRule(rules, CreateProcessRule(
                    blockedImageOrder,
                    memberFilter,
                    imageFilter,
                    newProcessFilter: 0,
                    policyId: null,
                    EspRuleAction.Block,
                    eventQueue,
                    new WespRuleActivityDescriptor(
                        WespActivityKind.Blocked,
                        WespActivityResourceKind.Process,
                        "Child application launch blocked",
                        imageName,
                        WespActivityProcessSource.NewProcess,
                        WespActivityTargetSource.NewProcessImage)));
            }

            var order = 300UL;
            AddRule(rules, CreateProcessRule(
                order++,
                memberFilter,
                imageFilter: 0,
                newProcessFilter: 0,
                policyId,
                EspRuleAction.ContinueMatchingNextRule,
                eventQueue: null,
                activity: null));
            AddRule(rules, CreateProcessRule(
                order++,
                memberFilter,
                imageFilter: 0,
                memberFilter,
                policyId: null,
                GetSuccessfulProcessActivityAction(),
                eventQueue,
                new WespRuleActivityDescriptor(
                    WespActivityKind.ProcessStarted,
                    WespActivityResourceKind.Process,
                    "Child application started",
                    string.Empty,
                    WespActivityProcessSource.NewProcess,
                    WespActivityTargetSource.NewProcessImage)));
            AddRule(rules, CreateProcessRule(
                order++,
                memberFilter,
                imageFilter: 0,
                newProcessFilter: 0,
                policyId: null,
                EspRuleAction.Block,
                eventQueue,
                new WespRuleActivityDescriptor(
                    WespActivityKind.Blocked,
                    WespActivityResourceKind.Process,
                    "Child launch blocked because WESP could not propagate its tag",
                    string.Empty,
                    WespActivityProcessSource.NewProcess,
                    WespActivityTargetSource.NewProcessImage)));

            var hasReadOnlyFileRules = policy.ReadOnlyFilePaths.Count != 0;
            var writableSectionFilter = !hasReadOnlyFileRules
                ? 0
                : AddFilter(filters, CreateIntegerFilter(WritablePageProtectionMask));
            var readOnlyCreateRuleSpecifications = !hasReadOnlyFileRules
                ? []
                : GetReadOnlyCreateRuleSpecifications();
            var readOnlyCreateRuleFilters = readOnlyCreateRuleSpecifications
                .Select(specification => AddFilter(
                    filters,
                    CreateIntegerFilter(specification.Value, specification.ComparisonType)))
                .ToArray();
            var hasProtectedFilePaths = policy.BlockedFilePaths.Count != 0 ||
                                        policy.ReadOnlyFilePaths.Count != 0;
            var renameInformationClassFilters = !hasProtectedFilePaths
                ? []
                : RenameFileInformationClasses
                    .Select(informationClass => AddFilter(
                        filters,
                        CreateIntegerFilter(informationClass, EspIntegerComparisonType.Equals)))
                    .ToArray();

            order = 1000;
            if (policy.BlockUncPaths)
            {
                foreach (var pathPattern in ExpandDirectoryPatterns(
                             UncNtPathRoot,
                             includeRootStreams: false))
                {
                    var pathFilter = AddFilter(
                        filters,
                        CreateFileObjectStringFilter(
                            FileObjectNormalizedNtPathProperty,
                            pathPattern.Value,
                            pathPattern.Pattern,
                            "UNC path"));
                    AddIoFullBlockRules(
                        rules,
                        ref order,
                        memberFilter,
                        pathFilter,
                        "UNC paths",
                        eventQueue);
                }
            }

            foreach (var path in policy.BlockedFilePaths)
            {
                foreach (var pathPattern in ExpandDirectoryPatterns(path))
                {
                    var pathFilter = AddFilter(filters, CreatePathFilter(pathPattern.Value, pathPattern.Pattern));
                    AddIoFullBlockRules(
                        rules,
                        ref order,
                        memberFilter,
                        pathFilter,
                        path,
                        eventQueue);
                }

                AddAncestorRenameBlockRules(
                    filters,
                    rules,
                    ref order,
                    memberFilter,
                    renameInformationClassFilters,
                    path,
                    eventQueue);
            }

            foreach (var path in policy.ReadOnlyFilePaths)
            {
                foreach (var pathPattern in ExpandDirectoryPatterns(path))
                {
                    var pathFilter = AddFilter(filters, CreatePathFilter(pathPattern.Value, pathPattern.Pattern));
                    AddIoWriteBlockRules(
                        rules,
                        ref order,
                        memberFilter,
                        pathFilter,
                        writableSectionFilter,
                        readOnlyCreateRuleSpecifications,
                        readOnlyCreateRuleFilters,
                        path,
                        eventQueue);
                }

                AddAncestorRenameBlockRules(
                    filters,
                    rules,
                    ref order,
                    memberFilter,
                    renameInformationClassFilters,
                    path,
                    eventQueue);
            }

            var registryWriteMaskFilter = policy.ReadOnlyRegistryKeys.Count == 0
                ? 0
                : AddFilter(filters, CreateIntegerFilter(RegistryWriteAccessMask));
            order = 5000;
            foreach (var path in policy.BlockedRegistryKeys)
            {
                foreach (var pathPattern in ExpandRegistryPatterns(path))
                {
                    var pathFilter = AddFilter(
                        filters,
                        CreateRegistryPathFilter(pathPattern.Value, pathPattern.Pattern));
                    AddRegistryBlockRules(
                        rules,
                        ref order,
                        memberFilter,
                        pathFilter,
                        path,
                        blockReads: true,
                        openWriteMaskFilter: 0,
                        eventQueue);
                }
            }

            foreach (var path in policy.ReadOnlyRegistryKeys)
            {
                foreach (var pathPattern in ExpandRegistryPatterns(path))
                {
                    var pathFilter = AddFilter(
                        filters,
                        CreateRegistryPathFilter(pathPattern.Value, pathPattern.Pattern));
                    AddRegistryBlockRules(
                        rules,
                        ref order,
                        memberFilter,
                        pathFilter,
                        path,
                        blockReads: false,
                        registryWriteMaskFilter,
                        eventQueue);
                }
            }

            var entries = rules
                .Select(rule => new NativeRuleUpdateEntry
                {
                    UpdateType = EspRuleUpdateType.Add,
                    Rule = rule
                })
                .ToArray();
            Check(
                NativeMethods.EspUpdateRules(client, flags: 0, checked((uint)entries.Length), entries),
                WespOperation.InstallRules,
                "WESP could not atomically install the policy rules.");
        }
        finally
        {
            foreach (var rule in rules)
            {
                _ = NativeMethods.EspCloseRule(rule);
            }

            foreach (var filter in filters)
            {
                _ = NativeMethods.EspCloseFilter(filter);
            }
        }
    }

    internal static void ValidateCapabilities(
        nint client,
        NormalizedWespPolicy policy)
    {
        foreach (var eventType in GetRequiredEvents(policy))
        {
            Check(
                NativeMethods.EspGetEventCapabilities(client, eventType, out var capabilities),
                WespOperation.CheckCapabilities,
                $"WESP could not query support for {eventType}.");
            var requiredCapabilities = eventType == EspEventType.ProcessCreate
                ? EspEventCapabilities.Monitor | EspEventCapabilities.Block
                : EspEventCapabilities.Block;
            if ((capabilities & requiredCapabilities) != requiredCapabilities)
            {
                throw new WespException(
                    WespOperation.CheckCapabilities,
                    $"The installed WESP build lacks required capabilities " +
                    $"({requiredCapabilities}) for {eventType}.");
            }
        }

        foreach (var property in GetRequiredFileObjectProperties(policy))
        {
            var description = property switch
            {
                FileObjectNormalizedNtPathProperty => "normalized NT file paths",
                FileObjectNormalizedDosPathProperty => "normalized DOS file paths",
                FileObjectFinalComponentProperty => "application file names",
                _ => $"file-object property {property}"
            };
            CheckProperty(
                NativeMethods.EspIsFileObjectPropertySupported(
                    client,
                    property,
                    out var pathSupported),
                pathSupported,
                description);
        }

        if (policy.BlockedRegistryKeys.Count != 0 ||
            policy.ReadOnlyRegistryKeys.Count != 0)
        {
            CheckProperty(
                NativeMethods.EspIsRegistryKeyPropertySupported(
                    client,
                    RegistryKeyNtPathProperty,
                    out var registryPathSupported),
                registryPathSupported,
                "canonical registry paths");
        }
    }

    internal static IReadOnlySet<EspEventType> GetRequiredEvents(
        NormalizedWespPolicy policy)
    {
        var events = new HashSet<EspEventType> { EspEventType.ProcessCreate };
        if (policy.BlockedFilePaths.Count != 0 || policy.BlockUncPaths)
        {
            events.UnionWith(
            [
                EspEventType.FileObjectCreate,
                EspEventType.FileObjectOpen,
                EspEventType.FileObjectRead,
                EspEventType.FileObjectWrite,
                EspEventType.FileSystemCreateFileSection,
                EspEventType.FileSystemQueryFileInformation,
                EspEventType.FileSystemSetFileInformation,
                EspEventType.FileSystemSetFileSecurity,
                EspEventType.FileSystemQueryDirectoryInformation,
                EspEventType.FileSystemControlFile,
                EspEventType.FileSystemSetExtendedAttributes,
                EspEventType.FileSystemLockFile
            ]);
        }

        if (policy.ReadOnlyFilePaths.Count != 0)
        {
            events.UnionWith(
            [
                EspEventType.FileObjectCreate,
                EspEventType.FileObjectOpen,
                EspEventType.FileObjectWrite,
                EspEventType.FileSystemCreateFileSection,
                EspEventType.FileSystemSetFileInformation,
                EspEventType.FileSystemSetFileSecurity,
                EspEventType.FileSystemControlFile,
                EspEventType.FileSystemSetExtendedAttributes
            ]);
        }

        if (policy.BlockedRegistryKeys.Count != 0)
        {
            events.UnionWith(RegistryMutationEvents);
            events.UnionWith(RegistryReadEvents);
            events.Add(EspEventType.RegistryCreateKey);
            events.Add(EspEventType.RegistryOpenKey);
        }

        if (policy.ReadOnlyRegistryKeys.Count != 0)
        {
            events.UnionWith(RegistryMutationEvents);
            events.Add(EspEventType.RegistryCreateKey);
            events.Add(EspEventType.RegistryOpenKey);
        }

        return events;
    }

    private static void CheckProperty(int hresult, int supported, string description)
    {
        Check(
            hresult,
            WespOperation.CheckCapabilities,
            $"WESP could not query support for {description}.");
        if (supported == 0)
        {
            throw new WespException(
                WespOperation.CheckCapabilities,
                $"The installed WESP build does not support {description}.");
        }
    }

    private static void AddIoFullBlockRules(
        List<nint> rules,
        ref ulong order,
        nint memberFilter,
        nint pathFilter,
        string configuredPath,
        WespEventQueue eventQueue)
    {
        AddRule(rules, CreateIoRule(order++, EspEventType.FileObjectCreate, memberFilter, pathFilter, eventQueue, FileActivity("File or folder open blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileObjectOpen, memberFilter, pathFilter, eventQueue, FileActivity("File or folder open blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileObjectRead, memberFilter, pathFilter, eventQueue, FileActivity("File read blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileObjectWrite, memberFilter, pathFilter, eventQueue, FileActivity("File write blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemCreateFileSection, memberFilter, pathFilter, eventQueue, FileActivity("File mapping blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemQueryFileInformation, memberFilter, pathFilter, eventQueue, FileActivity("File information query blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemSetFileInformation, memberFilter, pathFilter, eventQueue, FileActivity("File change blocked", configuredPath)));
        AddRule(rules, CreateIoRule(
            order++,
            EspEventType.FileSystemSetFileInformation,
            memberFilter,
            targetPathFilter: 0,
            eventQueue,
            FileActivity("Rename or move into folder blocked", configuredPath),
            parentTargetPathFilter: pathFilter));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemSetFileSecurity, memberFilter, pathFilter, eventQueue, FileActivity("File security change blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemQueryDirectoryInformation, memberFilter, pathFilter, eventQueue, FileActivity("Folder enumeration blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemControlFile, memberFilter, pathFilter, eventQueue, FileActivity("File-system control blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemSetExtendedAttributes, memberFilter, pathFilter, eventQueue, FileActivity("Extended-attribute change blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemLockFile, memberFilter, pathFilter, eventQueue, FileActivity("File lock blocked", configuredPath)));
    }

    private static void AddIoWriteBlockRules(
        List<nint> rules,
        ref ulong order,
        nint memberFilter,
        nint pathFilter,
        nint writableSectionFilter,
        IReadOnlyList<ReadOnlyCreateRuleSpecification> createRuleSpecifications,
        nint[] createRuleFilters,
        string configuredPath,
        WespEventQueue eventQueue)
    {
        if (createRuleSpecifications.Count != createRuleFilters.Length)
        {
            throw new InvalidOperationException("The read-only create rule plan and its filters are inconsistent.");
        }

        // WESP evaluates FO_CREATE BLOCK rules in pre-create and FO_OPEN BLOCK
        // rules in post-create. Only operations that can create, replace, truncate,
        // or delete on close are denied before the filesystem runs. An ordinary
        // FILE_OPEN that requests modification rights is denied after a successful
        // open, causing the original create request to complete with access denied.
        for (var index = 0; index < createRuleSpecifications.Count; index++)
        {
            var specification = createRuleSpecifications[index];
            var filter = createRuleFilters[index];
            AddRule(rules, CreateIoRule(
                order++,
                specification.EventType,
                memberFilter,
                pathFilter,
                eventQueue,
                FileActivity(DescribeReadOnlyCreateRule(specification.Field), configuredPath),
                firstArgumentFilter: specification.Field == ReadOnlyCreateRuleField.DesiredAccess ? filter : 0,
                secondArgumentFilter: specification.Field == ReadOnlyCreateRuleField.CreateOptions ? filter : 0,
                thirdArgumentFilter: specification.Field == ReadOnlyCreateRuleField.CreateDisposition ? filter : 0));
        }

        AddRule(rules, CreateIoRule(order++, EspEventType.FileObjectWrite, memberFilter, pathFilter, eventQueue, FileActivity("File write blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemCreateFileSection, memberFilter, pathFilter, eventQueue, FileActivity("Writable file mapping blocked", configuredPath), firstArgumentFilter: writableSectionFilter));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemSetFileInformation, memberFilter, pathFilter, eventQueue, FileActivity("File change blocked", configuredPath)));
        AddRule(rules, CreateIoRule(
            order++,
            EspEventType.FileSystemSetFileInformation,
            memberFilter,
            targetPathFilter: 0,
            eventQueue,
            FileActivity("Rename or move into folder blocked", configuredPath),
            parentTargetPathFilter: pathFilter));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemSetFileSecurity, memberFilter, pathFilter, eventQueue, FileActivity("File security change blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemControlFile, memberFilter, pathFilter, eventQueue, FileActivity("File-system control blocked", configuredPath)));
        AddRule(rules, CreateIoRule(order++, EspEventType.FileSystemSetExtendedAttributes, memberFilter, pathFilter, eventQueue, FileActivity("Extended-attribute change blocked", configuredPath)));
    }

    private static string DescribeReadOnlyCreateRule(ReadOnlyCreateRuleField field) => field switch
    {
        ReadOnlyCreateRuleField.DesiredAccess => "Write-capable file open blocked",
        ReadOnlyCreateRuleField.CreateOptions => "Delete-on-close open blocked",
        ReadOnlyCreateRuleField.CreateDisposition => "Creating or replacing a file blocked",
        _ => "File change blocked"
    };

    private static void AddAncestorRenameBlockRules(
        List<nint> filters,
        List<nint> rules,
        ref ulong order,
        nint memberFilter,
        nint[] renameInformationClassFilters,
        string protectedPath,
        WespEventQueue eventQueue)
    {
        foreach (var ancestorPath in GetRenamableAncestorDirectories(protectedPath))
        {
            var ancestorPathFilter = AddFilter(filters, CreatePathFilter(ancestorPath, pattern: false));
            foreach (var informationClassFilter in renameInformationClassFilters)
            {
                AddRule(rules, CreateIoRule(
                    order++,
                    EspEventType.FileSystemSetFileInformation,
                    memberFilter,
                    ancestorPathFilter,
                    eventQueue,
                    FileActivity("Protected folder ancestor rename blocked", protectedPath),
                    firstArgumentFilter: informationClassFilter));
            }
        }
    }

    private static void AddRegistryBlockRules(
        List<nint> rules,
        ref ulong order,
        nint memberFilter,
        nint pathFilter,
        string configuredPath,
        bool blockReads,
        nint openWriteMaskFilter,
        WespEventQueue eventQueue)
    {
        AddRule(rules, CreateRegistryRule(order++, EspEventType.RegistryCreateKey, memberFilter, pathFilter, desiredAccessFilter: 0, configuredPath, eventQueue));
        AddRule(rules, CreateRegistryRule(order++, EspEventType.RegistryOpenKey, memberFilter, pathFilter, blockReads ? 0 : openWriteMaskFilter, configuredPath, eventQueue));
        foreach (var eventType in RegistryMutationEvents)
        {
            AddRule(rules, CreateRegistryRule(order++, eventType, memberFilter, pathFilter, desiredAccessFilter: 0, configuredPath, eventQueue));
        }

        if (blockReads)
        {
            foreach (var eventType in RegistryReadEvents)
            {
                AddRule(rules, CreateRegistryRule(order++, eventType, memberFilter, pathFilter, desiredAccessFilter: 0, configuredPath, eventQueue));
            }
        }
    }

    private static unsafe nint CreateProcessRule(
        ulong order,
        nint creatingProcessFilter,
        nint imageFilter,
        nint newProcessFilter,
        ulong? policyId,
        EspRuleAction action,
        WespEventQueue? eventQueue,
        WespRuleActivityDescriptor? activity)
    {
        NativeProcessCreateConfig config = default;
        config.CreatingThreadOwningProcessFilter = creatingProcessFilter;
        config.ImageFileObjectFilter = imageFilter;
        config.ImageFileObjectType = imageFilter == 0 ? 0 : 1;
        config.NewProcessFilter = newProcessFilter;

        fixed (uint* activityProperties = ActivityProcessProperties)
        {
            if (eventQueue is not null && activity is not null)
            {
                config.NewProcessIncludeInNotification = 1;
                config.NewProcessPropertiesCount = (uint)ActivityProcessProperties.Length;
                config.NewProcessProperties = (nint)activityProperties;
            }

            NativeContextKeyUpdate update = default;
            if (policyId.HasValue)
            {
                update = CreateContextUpdate(policyId.Value);
                config.NewProcessContext = new NativeContextKeyConfig
                {
                    UpdatesCount = 1,
                    Updates = (nint)(&update)
                };
            }

            return CreateRule(
                order,
                EspEventType.ProcessCreate,
                action,
                &config,
                currentProcessFilter: 0,
                eventQueue,
                activity);
        }
    }

    private static unsafe nint CreateIoRule(
        ulong order,
        EspEventType eventType,
        nint requestorProcessFilter,
        nint targetPathFilter,
        WespEventQueue eventQueue,
        WespRuleActivityDescriptor activity,
        nint firstArgumentFilter = 0,
        nint secondArgumentFilter = 0,
        nint thirdArgumentFilter = 0,
        nint parentTargetPathFilter = 0)
    {
        NativeIoEventConfig config = default;
        config.RequestorProcessFilter = requestorProcessFilter;
        config.RequestorProcessIncludeInNotification = 1;
        config.TargetFileObjectFilter = targetPathFilter;
        config.TargetFileObjectIncludeInNotification = 1;
        config.TargetFileObjectType = targetPathFilter == 0 ? 0 : 1;
        config.FirstArgumentFilter = firstArgumentFilter;
        config.SecondArgumentFilter = secondArgumentFilter;
        config.ThirdArgumentFilter = thirdArgumentFilter;
        config.ParentTargetFileObjectFilter = parentTargetPathFilter;
        config.ParentTargetFileObjectType = parentTargetPathFilter == 0 ? 0 : 1;
        fixed (uint* activityProperties = ActivityProcessProperties)
        fixed (uint* targetProperties = ActivityFileTargetProperties)
        {
            config.RequestorProcessPropertiesCount = (uint)ActivityProcessProperties.Length;
            config.RequestorProcessProperties = (nint)activityProperties;
            config.TargetFileObjectPropertiesCount = (uint)ActivityFileTargetProperties.Length;
            config.TargetFileObjectProperties = (nint)targetProperties;
            return CreateRule(
                order,
                eventType,
                EspRuleAction.Block,
                &config,
                currentProcessFilter: 0,
                eventQueue,
                activity);
        }
    }

    private static unsafe nint CreateRegistryRule(
        ulong order,
        EspEventType eventType,
        nint currentProcessFilter,
        nint registryPathFilter,
        nint desiredAccessFilter,
        string configuredPath,
        WespEventQueue eventQueue)
    {
        NativeRegistryEventConfig config = default;
        var targetSource = GetRegistryActivityTargetSource(eventType);
        fixed (uint* targetProperties = ActivityRegistryTargetProperties)
        {
            if (eventType == EspEventType.RegistryCreateKey)
            {
                config.RegistryKeyFilter = registryPathFilter;
                config.RegistryKeyIncludeInNotification = 1;
                config.RegistryKeyPropertiesCount = (uint)ActivityRegistryTargetProperties.Length;
                config.RegistryKeyProperties = (nint)targetProperties;
                config.CreateDesiredAccessFilter = desiredAccessFilter;
            }
            else if (eventType == EspEventType.RegistryOpenKey)
            {
                config.RegistryKeyFilter = registryPathFilter;
                config.RegistryKeyIncludeInNotification = 1;
                config.RegistryKeyPropertiesCount = (uint)ActivityRegistryTargetProperties.Length;
                config.RegistryKeyProperties = (nint)targetProperties;
                config.OpenDesiredAccessFilter = desiredAccessFilter;
            }
            else
            {
                config.RegistryKeyObjectKeyFilter = registryPathFilter;
                config.RegistryKeyObjectKeyIncludeInNotification = 1;
                config.RegistryKeyObjectKeyPropertiesCount = (uint)ActivityRegistryTargetProperties.Length;
                config.RegistryKeyObjectKeyProperties = (nint)targetProperties;
            }

            return CreateRule(
                order,
                eventType,
                EspRuleAction.Block,
                &config,
                currentProcessFilter,
                eventQueue,
                new WespRuleActivityDescriptor(
                    WespActivityKind.Blocked,
                    WespActivityResourceKind.Registry,
                    DescribeRegistryOperation(eventType),
                    configuredPath,
                    WespActivityProcessSource.CurrentProcess,
                    targetSource));
        }
    }

    private static unsafe nint CreateRule(
        ulong order,
        EspEventType eventType,
        EspRuleAction action,
        void* eventConfig,
        nint currentProcessFilter,
        WespEventQueue? eventQueue,
        WespRuleActivityDescriptor? activity)
    {
        var captureActivity = (action is EspRuleAction.Block or EspRuleAction.Notify) &&
                              eventQueue is not null &&
                              activity is not null;
        var ruleId = captureActivity ? Guid.NewGuid() : Guid.Empty;
        fixed (uint* activityProperties = ActivityProcessProperties)
        {
            var descriptor = new NativeRuleDescriptor
            {
                RuleId = ruleId,
                OrderGroup = order,
                Lifetime = EspRuleLifetime.ClientSession,
                EventType = eventType,
                EventTypeConfig = (nint)eventConfig,
                CurrentProcessFilter = currentProcessFilter,
                CurrentProcessIncludeInNotification = captureActivity &&
                                                      activity!.ProcessSource == WespActivityProcessSource.CurrentProcess
                    ? 1
                    : 0,
                CurrentProcessPropertiesCount = captureActivity &&
                                                activity!.ProcessSource == WespActivityProcessSource.CurrentProcess
                    ? (uint)ActivityProcessProperties.Length
                    : 0,
                CurrentProcessProperties = captureActivity &&
                                           activity!.ProcessSource == WespActivityProcessSource.CurrentProcess
                    ? (nint)activityProperties
                    : 0
            };
            ConfigureRuleAction(
                ref descriptor,
                action,
                captureActivity ? eventQueue!.Handle : 0);

            Check(
                NativeMethods.EspCreateRule(in descriptor, out var rule),
                WespOperation.CreateRule,
                $"WESP could not create the {eventType} rule.");
            if (captureActivity)
            {
                eventQueue!.RegisterRule(ruleId, activity!);
            }

            return rule;
        }
    }

    internal static void ConfigureRuleAction(
        ref NativeRuleDescriptor descriptor,
        EspRuleAction action,
        nint asyncEventQueue)
    {
        descriptor.Action = action;
        if (action == EspRuleAction.Block)
        {
            descriptor.BlockReason = EspBlockReason.AccessDenied;
            descriptor.BlockAsyncEventQueue = asyncEventQueue;
        }
        else if (action == EspRuleAction.Notify)
        {
            descriptor.NotifyAsyncEventQueue = asyncEventQueue;
        }
    }

    private static nint CreateContextFilter(ulong policyId)
    {
        var comparison = new NativeContextKeyComparison
        {
            ComparisonType = EspContextKeyComparisonType.Integer,
            IntegerComparisonType = EspIntegerComparisonType.Equals,
            SourceType = EspValueSourceType.Raw,
            RawValue = policyId
        };
        Check(
            NativeMethods.EspCreateProcessFilter(
                ProcessContextPropertyBase | PolicyContextKey,
                EspComparisonType.ContextKey,
                in comparison,
                out var filter),
            WespOperation.CreateFilter,
            "WESP could not create the policy-membership filter.");
        return filter;
    }

    private static nint CreateIntegerFilter(
        ulong value,
        EspIntegerComparisonType comparisonType = EspIntegerComparisonType.IsAnyFlagSet)
    {
        var comparison = new NativeIntegerComparison
        {
            ComparisonType = comparisonType,
            SourceType = EspValueSourceType.Raw,
            RawValue = value
        };
        Check(
            NativeMethods.EspCreateFilter(
                EspComparisonType.Integer,
                in comparison,
                out var filter),
            WespOperation.CreateFilter,
            "WESP could not create an access filter.");
        return filter;
    }

    private static nint CreatePathFilter(string path, bool pattern) =>
        CreateFileObjectStringFilter(
            FileObjectNormalizedDosPathProperty,
            path,
            pattern,
            "path");

    private static unsafe nint CreateFileObjectStringFilter(
        uint propertyId,
        string value,
        bool pattern,
        string description)
    {
        fixed (char* buffer = value)
        {
            var comparison = CreateStringComparison(value, pattern, (nint)buffer);
            Check(
                NativeMethods.EspCreateFileObjectFilter(
                    propertyId,
                    EspComparisonType.String,
                    in comparison,
                    out var filter),
                WespOperation.CreateFilter,
                $"WESP could not create a {description} filter for '{value}'.");
            return filter;
        }
    }

    private static unsafe nint CreateRegistryPathFilter(string path, bool pattern)
    {
        fixed (char* buffer = path)
        {
            var comparison = CreateStringComparison(path, pattern, (nint)buffer);
            Check(
                NativeMethods.EspCreateRegistryKeyFilter(
                    RegistryKeyNtPathProperty,
                    EspComparisonType.String,
                    in comparison,
                    out var filter),
                WespOperation.CreateFilter,
                $"WESP could not create a registry-key filter for '{path}'.");
            return filter;
        }
    }

    private static WespRuleActivityDescriptor FileActivity(
        string operation,
        string configuredPath) =>
        new(
            WespActivityKind.Blocked,
            WespActivityResourceKind.File,
            operation,
            configuredPath,
            WespActivityProcessSource.RequestorProcess,
            WespActivityTargetSource.IoTargetFileObject);

    private static string DescribeRegistryOperation(EspEventType eventType) =>
        eventType switch
        {
            EspEventType.RegistryCreateKey => "Registry key creation blocked",
            EspEventType.RegistryOpenKey => "Registry key open blocked",
            EspEventType.RegistryDeleteKey => "Registry key deletion blocked",
            EspEventType.RegistrySetValue => "Registry value write blocked",
            EspEventType.RegistryDeleteValue => "Registry value deletion blocked",
            EspEventType.RegistryRenameKey => "Registry key rename blocked",
            EspEventType.RegistryReplaceKey => "Registry key replacement blocked",
            EspEventType.RegistryRestoreKey => "Registry key restore blocked",
            EspEventType.RegistrySetKeySecurity => "Registry security change blocked",
            EspEventType.RegistryQueryKey => "Registry key query blocked",
            EspEventType.RegistryQueryValue => "Registry value query blocked",
            EspEventType.RegistrySaveKey => "Registry key save blocked",
            EspEventType.RegistryLoadKey => "Registry hive load blocked",
            EspEventType.RegistryEnumerateKey => "Registry key enumeration blocked",
            EspEventType.RegistryEnumerateValue => "Registry value enumeration blocked",
            _ => "Registry operation blocked"
        };

    private static NativeContextKeyUpdate CreateContextUpdate(ulong policyId) => new()
    {
        ContextKey = PolicyContextKey,
        Lifetime = EspContextKeyLifetime.ClientSession,
        UpdateType = EspContextKeyUpdateType.CreateOrReplace,
        ValueType = EspContextKeyValueType.Integer,
        IntegerValue = policyId
    };

    internal static NativeContextKeyUpdate CreateRootTagUpdate(ulong policyId) =>
        CreateContextUpdate(policyId);

    // Describes both phases of IRP_MJ_CREATE enforcement. FO_CREATE entries are
    // evaluated pre-create; the FO_OPEN entry is evaluated post-create.
    internal static IReadOnlyList<ReadOnlyCreateRuleSpecification> GetReadOnlyCreateRuleSpecifications()
    {
        var specifications = new List<ReadOnlyCreateRuleSpecification>(
            MutatingCreateDispositions.Length + 2)
        {
            new(
                EspEventType.FileObjectCreate,
                ReadOnlyCreateRuleField.CreateOptions,
                EspIntegerComparisonType.IsAnyFlagSet,
                DeleteOnCloseCreateOption)
        };
        specifications.AddRange(MutatingCreateDispositions.Select(disposition =>
            new ReadOnlyCreateRuleSpecification(
                EspEventType.FileObjectCreate,
                ReadOnlyCreateRuleField.CreateDisposition,
                EspIntegerComparisonType.Equals,
                disposition)));
        specifications.Add(new(
            EspEventType.FileObjectOpen,
            ReadOnlyCreateRuleField.DesiredAccess,
            EspIntegerComparisonType.IsAnyFlagSet,
            WriteAccessMask));
        return specifications;
    }

    internal static IReadOnlyList<ulong> GetRenameFileInformationClasses() =>
        RenameFileInformationClasses;

    internal static IEnumerable<string> GetRenamableAncestorDirectories(string path)
    {
        var pathRoot = Path.GetPathRoot(path);
        for (var ancestor = Path.GetDirectoryName(path);
             !string.IsNullOrEmpty(ancestor) &&
             !string.Equals(ancestor, pathRoot, StringComparison.OrdinalIgnoreCase);
             ancestor = Path.GetDirectoryName(ancestor))
        {
            yield return ancestor;
        }
    }

    internal static IEnumerable<(string Value, bool Pattern)> ExpandDirectoryPatterns(
        string path,
        bool includeRootStreams = true)
    {
        yield return (path, false);
        var literalPath = EscapePatternLiteral(path);
        yield return (literalPath.EndsWith(Path.DirectorySeparatorChar)
            ? literalPath + "*"
            : literalPath + Path.DirectorySeparatorChar + "*", true);
        if (includeRootStreams)
        {
            // A normalized minifilter name retains a named NTFS stream as
            // "path:stream". The exact-root and descendant patterns do not match
            // that name, so protect streams attached directly to the root too.
            yield return (literalPath + ":*", true);
        }
    }

    internal static IReadOnlyList<string> GetBlockedChildImageNames(
        NormalizedWespPolicy policy) =>
        policy.BlockedChildExecutables
            .Select(GetBlockedChildImageName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string GetBlockedChildImageName(string path)
    {
        var imageName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(imageName))
        {
            throw new WespException(
                WespOperation.ValidatePolicy,
                $"A blocked child application path must include a file name: {path}");
        }

        return imageName;
    }

    internal static IReadOnlySet<uint> GetRequiredFileObjectProperties(
        NormalizedWespPolicy policy)
    {
        var properties = new HashSet<uint>();
        if (policy.BlockedFilePaths.Count != 0 ||
            policy.ReadOnlyFilePaths.Count != 0 ||
            policy.BlockUncPaths)
        {
            properties.Add(FileObjectNormalizedNtPathProperty);
            properties.Add(FileObjectNormalizedDosPathProperty);
        }

        if (policy.BlockedChildExecutables.Count != 0)
        {
            properties.Add(FileObjectFinalComponentProperty);
        }

        return properties;
    }

    internal static IReadOnlyList<uint> GetActivityProcessProperties() =>
        ActivityProcessProperties.ToArray();

    internal static IReadOnlyList<uint> GetActivityFileTargetProperties() =>
        ActivityFileTargetProperties.ToArray();

    internal static IReadOnlyList<uint> GetActivityRegistryTargetProperties() =>
        ActivityRegistryTargetProperties.ToArray();

    internal static WespActivityTargetSource GetRegistryActivityTargetSource(
        EspEventType eventType)
    {
        if ((int)eventType is < (int)EspEventType.RegistryCreateKey or
            > (int)EspEventType.RegistryEnumerateValue)
        {
            throw new ArgumentOutOfRangeException(nameof(eventType));
        }

        return eventType is EspEventType.RegistryCreateKey or EspEventType.RegistryOpenKey
            ? WespActivityTargetSource.RegistryKey
            : WespActivityTargetSource.RegistryKeyObject;
    }

    internal static EspRuleAction GetSuccessfulProcessActivityAction() =>
        EspRuleAction.Notify;

    internal static IEnumerable<(string Value, bool Pattern)> ExpandRegistryPatterns(string path)
    {
        yield return (path, false);
        yield return (EscapePatternLiteral(path) + "\\*", true);
    }

    internal static string EscapePatternLiteral(string value) =>
        value
            .Replace("|", "||", StringComparison.Ordinal)
            .Replace("*", "|*", StringComparison.Ordinal)
            .Replace("?", "|?", StringComparison.Ordinal);

    internal static NativeStringComparison CreateStringComparison(
        string value,
        bool pattern,
        nint buffer)
    {
        if (value.Length > ushort.MaxValue / sizeof(char))
        {
            throw new WespException(
                WespOperation.ValidatePolicy,
                $"The value is too long for a WESP string filter: {value}");
        }

        return new NativeStringComparison
        {
            ComparisonType = pattern
                ? EspStringComparisonType.PatternMatch
                : EspStringComparisonType.Equals,
            SourceType = EspValueSourceType.Raw,
            CaseSensitive = 0,
            StringLengthBytes = checked((ushort)(value.Length * sizeof(char))),
            StringBuffer = buffer
        };
    }

    private static nint AddFilter(List<nint> filters, nint filter)
    {
        filters.Add(filter);
        return filter;
    }

    private static void AddRule(List<nint> rules, nint rule) => rules.Add(rule);

    private static void Check(int hresult, WespOperation operation, string detail)
    {
        if (hresult < 0)
        {
            throw WespException.FromHResult(operation, hresult, detail);
        }
    }
}
