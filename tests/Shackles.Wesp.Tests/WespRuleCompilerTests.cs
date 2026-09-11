using Shackles.Wesp.Internal;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespRuleCompilerTests
{
    private static readonly string[] ProtectedFolderAncestors =
        [@"C:\Parent\Child", @"C:\Parent"];

    private static readonly ulong[] RenameFileInformationClasses =
    [
        10, // FileRenameInformation
        56, // FileRenameInformationBypassAccessCheck
        65, // FileRenameInformationEx
        66  // FileRenameInformationExBypassAccessCheck
    ];

    [TestMethod]
    public void EscapePatternLiteralEscapesEveryWespWildcardMetacharacter()
    {
        Assert.AreEqual(
            @"C:\literal|*question|?pipe||segment",
            WespRuleCompiler.EscapePatternLiteral(@"C:\literal*question?pipe|segment"));
        Assert.AreEqual(
            @"\REGISTRY\MACHINE\Software\Shackles.Tests",
            WespRuleCompiler.EscapePatternLiteral(@"\REGISTRY\MACHINE\Software\Shackles.Tests"));
    }

    [TestMethod]
    public void DirectoryPatternsMatchTheExactFolderAndItsDescendantsSeparately()
    {
        var patterns = WespRuleCompiler
            .ExpandDirectoryPatterns(@"C:\Blocked")
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                (@"C:\Blocked", false),
                (@"C:\Blocked\*", true),
                (@"C:\Blocked:*", true)
            },
            patterns);
    }

    [TestMethod]
    public void DirectoryPatternHandlesDriveRootsWithoutAddingAnotherSeparator()
    {
        var patterns = WespRuleCompiler
            .ExpandDirectoryPatterns(@"C:\")
            .ToArray();

        Assert.AreEqual((@"C:\", false), patterns[0]);
        Assert.AreEqual((@"C:\*", true), patterns[1]);
        Assert.AreEqual((@"C:\:*", true), patterns[2]);
    }

    [TestMethod]
    public void ProtectedFolderAncestorsExcludeTheVolumeRoot()
    {
        CollectionAssert.AreEqual(
            ProtectedFolderAncestors,
            WespRuleCompiler
                .GetRenamableAncestorDirectories(@"C:\Parent\Child\Protected")
                .ToArray());
        Assert.HasCount(
            0,
            WespRuleCompiler.GetRenamableAncestorDirectories(@"C:\Protected"));
    }

    [TestMethod]
    public void AncestorRenameRulesCoverEveryWindowsRenameInformationClass()
    {
        CollectionAssert.AreEqual(
            RenameFileInformationClasses,
            WespRuleCompiler.GetRenameFileInformationClasses().ToArray());
    }

    [TestMethod]
    public void UncDevicePatternsMatchTheMupRootAndDescendantsSeparately()
    {
        var patterns = WespRuleCompiler
            .ExpandDirectoryPatterns(
                WespRuleCompiler.UncNtPathRoot,
                includeRootStreams: false)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                (@"\Device\Mup", false),
                (@"\Device\Mup\*", true)
            },
            patterns);
    }

    [TestMethod]
    public void RegistryPatternsEscapeLiteralMetacharactersBeforeAddingDescendants()
    {
        var patterns = WespRuleCompiler
            .ExpandRegistryPatterns(@"\REGISTRY\MACHINE\Software\literal*question?pipe|segment")
            .ToArray();

        Assert.AreEqual(
            (@"\REGISTRY\MACHINE\Software\literal*question?pipe|segment", false),
            patterns[0]);
        Assert.AreEqual(
            (@"\REGISTRY\MACHINE\Software\literal|*question|?pipe||segment\*", true),
            patterns[1]);
    }

    [TestMethod]
    public void NativeStringComparisonsUseCaseInsensitiveRawWespStrings()
    {
        var comparison = WespRuleCompiler.CreateStringComparison(
            @"C:\Blocked\*",
            pattern: true,
            buffer: (nint)42);

        Assert.AreEqual(EspStringComparisonType.PatternMatch, comparison.ComparisonType);
        Assert.AreEqual(EspValueSourceType.Raw, comparison.SourceType);
        Assert.AreEqual(0, comparison.CaseSensitive);
        Assert.AreEqual(
            (ushort)(@"C:\Blocked\*".Length * sizeof(char)),
            comparison.StringLengthBytes);
        Assert.AreEqual((nint)42, comparison.StringBuffer);
    }

    [TestMethod]
    public void BlockedChildImageNamesUseCaseInsensitiveExactComparisons()
    {
        var comparison = WespRuleCompiler.CreateStringComparison(
            "powershell.exe",
            pattern: false,
            buffer: (nint)42);

        Assert.AreEqual(EspStringComparisonType.Equals, comparison.ComparisonType);
        Assert.AreEqual(EspValueSourceType.Raw, comparison.SourceType);
        Assert.AreEqual(0, comparison.CaseSensitive);
    }

    [TestMethod]
    public void BlockedFilesRequireReadAndWriteRelatedEvents()
    {
        var events = WespRuleCompiler.GetRequiredEvents(Policy(
            blockedFiles: [@"C:\blocked"]));

        Assert.IsTrue(events.Contains(EspEventType.FileObjectRead));
        Assert.IsTrue(events.Contains(EspEventType.FileObjectWrite));
        Assert.IsTrue(events.Contains(EspEventType.FileSystemQueryFileInformation));
        Assert.IsTrue(events.Contains(EspEventType.FileSystemSetFileInformation));
        Assert.IsTrue(events.Contains(EspEventType.FileSystemQueryDirectoryInformation));
        Assert.IsTrue(events.Contains(EspEventType.FileSystemSetFileSecurity));
    }

    [TestMethod]
    public void UncBlockingRequiresTheFullBlockedFileEventSet()
    {
        var events = WespRuleCompiler.GetRequiredEvents(Policy(
            blockUncPaths: true));

        Assert.IsTrue(events.Contains(EspEventType.FileObjectRead));
        Assert.IsTrue(events.Contains(EspEventType.FileObjectWrite));
        Assert.IsTrue(events.Contains(EspEventType.FileSystemQueryFileInformation));
        Assert.IsTrue(events.Contains(EspEventType.FileSystemSetFileInformation));
        Assert.IsTrue(events.Contains(EspEventType.FileSystemQueryDirectoryInformation));
        Assert.IsTrue(events.Contains(EspEventType.FileSystemSetFileSecurity));
    }

    [TestMethod]
    public void BlockedChildExecutablesCompileToDistinctFileNames()
    {
        var imageNames = WespRuleCompiler.GetBlockedChildImageNames(Policy(
            blockedChildExecutables:
            [
                @"C:\Windows\powershell.exe",
                @"C:\Windows\System32\POWERSHELL.EXE",
                @"D:\Tools\cmd.exe"
            ]));

        Assert.HasCount(2, imageNames);
        Assert.AreEqual("powershell.exe", imageNames[0]);
        Assert.AreEqual("cmd.exe", imageNames[1]);
    }

    [TestMethod]
    public void RequiredFileObjectPropertiesReflectEachConfiguredRuleType()
    {
        var properties = WespRuleCompiler.GetRequiredFileObjectProperties(Policy(
            blockedFiles: [@"C:\blocked"],
            blockedChildExecutables: [@"C:\Tools\blocked.exe"],
            blockUncPaths: true));

        CollectionAssert.AreEquivalent(
            new uint[]
            {
                WespRuleCompiler.FileObjectNormalizedNtPathProperty,
                WespRuleCompiler.FileObjectNormalizedDosPathProperty,
                WespRuleCompiler.FileObjectFinalComponentProperty
            },
            properties.ToArray());
    }

    [TestMethod]
    public void FileRulesRequestBothDisplayPathFormsForActivityCapture()
    {
        AssertRequiredFileObjectProperties(Policy());
        AssertRequiredFileObjectProperties(
            Policy(blockedFiles: [@"C:\blocked"]),
            WespRuleCompiler.FileObjectNormalizedNtPathProperty,
            WespRuleCompiler.FileObjectNormalizedDosPathProperty);
        AssertRequiredFileObjectProperties(
            Policy(blockUncPaths: true),
            WespRuleCompiler.FileObjectNormalizedNtPathProperty,
            WespRuleCompiler.FileObjectNormalizedDosPathProperty);
        AssertRequiredFileObjectProperties(
            Policy(blockedChildExecutables: [@"C:\Tools\blocked.exe"]),
            WespRuleCompiler.FileObjectFinalComponentProperty);
    }

    [TestMethod]
    public void ReadOnlyFilesRequireCreateOpenAndMutationEventsButNotReads()
    {
        var events = WespRuleCompiler.GetRequiredEvents(Policy(
            readOnlyFiles: [@"C:\read-only"]));

        EspEventType[] required =
        [
            EspEventType.FileObjectCreate,
            EspEventType.FileObjectOpen,
            EspEventType.FileObjectWrite,
            EspEventType.FileSystemCreateFileSection,
            EspEventType.FileSystemSetFileInformation,
            EspEventType.FileSystemSetFileSecurity,
            EspEventType.FileSystemControlFile,
            EspEventType.FileSystemSetExtendedAttributes
        ];
        EspEventType[] permittedReads =
        [
            EspEventType.FileObjectRead,
            EspEventType.FileSystemQueryFileInformation,
            EspEventType.FileSystemQueryDirectoryInformation
        ];

        foreach (var eventType in required)
        {
            Assert.IsTrue(events.Contains(eventType), $"Missing required read-only event {eventType}.");
        }

        foreach (var eventType in permittedReads)
        {
            Assert.IsFalse(events.Contains(eventType), $"Read-only policy unexpectedly requires {eventType}.");
        }
    }

    [TestMethod]
    public void ReadOnlyPreCreateRulesContainOnlyDestructiveCreateSemantics()
    {
        var rulePlan = WespRuleCompiler.GetReadOnlyCreateRuleSpecifications();

        Assert.HasCount(7, rulePlan);
        var specifications = rulePlan
            .Where(specification => specification.EventType == EspEventType.FileObjectCreate)
            .ToArray();

        Assert.HasCount(6, specifications);
        Assert.IsFalse(
            specifications.Any(specification =>
                specification.Field == ReadOnlyCreateRuleField.DesiredAccess),
            "Write-capable ordinary opens must be denied in FO_OPEN post-create, not FO_CREATE pre-create.");

        AssertRuleSpecification(
            specifications,
            ReadOnlyCreateRuleField.CreateOptions,
            EspIntegerComparisonType.IsAnyFlagSet,
            0x00001000); // FILE_DELETE_ON_CLOSE

        foreach (var disposition in new ulong[]
                 {
                     0, // FILE_SUPERSEDE
                     2, // FILE_CREATE
                     3, // FILE_OPEN_IF
                     4, // FILE_OVERWRITE
                     5  // FILE_OVERWRITE_IF
                 })
        {
            AssertRuleSpecification(
                specifications,
                ReadOnlyCreateRuleField.CreateDisposition,
                EspIntegerComparisonType.Equals,
                disposition);
        }
    }

    [TestMethod]
    public void ReadOnlyPostCreateRuleDeniesModificationCapableOpen()
    {
        const ulong writeAccessMask =
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

        var specifications = WespRuleCompiler
            .GetReadOnlyCreateRuleSpecifications()
            .Where(specification => specification.EventType == EspEventType.FileObjectOpen)
            .ToArray();

        Assert.HasCount(1, specifications);
        AssertRuleSpecification(
            specifications,
            ReadOnlyCreateRuleField.DesiredAccess,
            EspIntegerComparisonType.IsAnyFlagSet,
            writeAccessMask);
    }

    [TestMethod]
    public void BlockedRegistryRequiresEveryRegistryEvent()
    {
        var events = WespRuleCompiler.GetRequiredEvents(Policy(
            blockedRegistry: [@"\REGISTRY\MACHINE\Software\Blocked"]));
        var registryEvents = Enum.GetValues<EspEventType>()
            .Where(eventType => (int)eventType is >= 7000 and <= 7014)
            .ToArray();

        Assert.HasCount(15, registryEvents);
        foreach (var eventType in registryEvents)
        {
            Assert.IsTrue(events.Contains(eventType), $"Missing required registry event {eventType}.");
        }
    }

    [TestMethod]
    public void ReadOnlyRegistryRequiresMutationsButNotQueriesEnumerationOrSave()
    {
        var events = WespRuleCompiler.GetRequiredEvents(Policy(
            readOnlyRegistry: [@"\REGISTRY\MACHINE\Software\ReadOnly"]));

        EspEventType[] requiredMutations =
        [
            EspEventType.RegistryCreateKey,
            EspEventType.RegistryOpenKey,
            EspEventType.RegistryDeleteKey,
            EspEventType.RegistrySetValue,
            EspEventType.RegistryDeleteValue,
            EspEventType.RegistryRenameKey,
            EspEventType.RegistryReplaceKey,
            EspEventType.RegistryRestoreKey,
            EspEventType.RegistrySetKeySecurity,
            EspEventType.RegistryLoadKey
        ];
        EspEventType[] permittedReads =
        [
            EspEventType.RegistryQueryKey,
            EspEventType.RegistryQueryValue,
            EspEventType.RegistrySaveKey,
            EspEventType.RegistryEnumerateKey,
            EspEventType.RegistryEnumerateValue
        ];

        foreach (var eventType in requiredMutations)
        {
            Assert.IsTrue(events.Contains(eventType), $"Missing required mutation event {eventType}.");
        }

        foreach (var eventType in permittedReads)
        {
            Assert.IsFalse(events.Contains(eventType), $"Read-only policy unexpectedly requires {eventType}.");
        }
    }

    [TestMethod]
    public void EmptyPolicyRequiresOnlyProcessCreationForMembershipPropagation()
    {
        var events = WespRuleCompiler.GetRequiredEvents(Policy());

        Assert.HasCount(1, events);
        Assert.IsTrue(events.Contains(EspEventType.ProcessCreate));
    }

    [TestMethod]
    public void ActivityNotificationsCapturePidAndImagePath()
    {
        var properties = WespRuleCompiler.GetActivityProcessProperties();

        CollectionAssert.AreEqual(
            new uint[]
            {
                WespRuleCompiler.ProcessIdProperty,
                WespRuleCompiler.ProcessImageNtPathProperty,
                WespRuleCompiler.ProcessImageDosPathProperty
            },
            properties.ToArray());
    }

    [TestMethod]
    public void ActivityNotificationsCaptureFileAndRegistryTargets()
    {
        CollectionAssert.AreEqual(
            new uint[]
            {
                WespRuleCompiler.FileObjectNormalizedNtPathProperty,
                WespRuleCompiler.FileObjectNormalizedDosPathProperty
            },
            WespRuleCompiler.GetActivityFileTargetProperties().ToArray());
        CollectionAssert.AreEqual(
            new uint[]
            {
                WespRuleCompiler.RegistryKeyNtPathProperty
            },
            WespRuleCompiler.GetActivityRegistryTargetProperties().ToArray());
    }

    [TestMethod]
    public void RegistryActivityUsesTheEventSpecificKeyShape()
    {
        Assert.AreEqual(
            WespActivityTargetSource.RegistryKey,
            WespRuleCompiler.GetRegistryActivityTargetSource(
                EspEventType.RegistryCreateKey));
        Assert.AreEqual(
            WespActivityTargetSource.RegistryKey,
            WespRuleCompiler.GetRegistryActivityTargetSource(
                EspEventType.RegistryOpenKey));

        foreach (var eventType in Enum.GetValues<EspEventType>()
                     .Where(eventType => (int)eventType is >= 7002 and <= 7014))
        {
            Assert.AreEqual(
                WespActivityTargetSource.RegistryKeyObject,
                WespRuleCompiler.GetRegistryActivityTargetSource(eventType),
                $"Unexpected target source for {eventType}.");
        }
    }

    [TestMethod]
    public void SuccessfulChildActivityUsesNonBlockingNotifyAction()
    {
        Assert.AreEqual(
            EspRuleAction.Notify,
            WespRuleCompiler.GetSuccessfulProcessActivityAction());
    }

    [TestMethod]
    public void BlockAndNotifyActionsUseTheirDistinctQueueUnionMembers()
    {
        NativeRuleDescriptor block = default;
        WespRuleCompiler.ConfigureRuleAction(
            ref block,
            EspRuleAction.Block,
            (nint)42);
        Assert.AreEqual(EspBlockReason.AccessDenied, block.BlockReason);
        Assert.AreEqual((nint)42, block.BlockAsyncEventQueue);

        NativeRuleDescriptor notify = default;
        WespRuleCompiler.ConfigureRuleAction(
            ref notify,
            EspRuleAction.Notify,
            (nint)43);
        Assert.AreEqual((nint)43, notify.NotifyAsyncEventQueue);
    }

    private static NormalizedWespPolicy Policy(
        IReadOnlyList<string>? blockedFiles = null,
        IReadOnlyList<string>? readOnlyFiles = null,
        IReadOnlyList<string>? blockedRegistry = null,
        IReadOnlyList<string>? readOnlyRegistry = null,
        IReadOnlyList<string>? blockedChildExecutables = null,
        bool blockUncPaths = false) => new(
            BlockedFilePaths: blockedFiles ?? [],
            ReadOnlyFilePaths: readOnlyFiles ?? [],
            BlockedRegistryKeys: blockedRegistry ?? [],
            ReadOnlyRegistryKeys: readOnlyRegistry ?? [],
            BlockedChildExecutables: blockedChildExecutables ?? [],
            BlockUncPaths: blockUncPaths);

    private static void AssertRequiredFileObjectProperties(
        NormalizedWespPolicy policy,
        params uint[] expected)
    {
        var actual = WespRuleCompiler.GetRequiredFileObjectProperties(policy);
        Assert.HasCount(expected.Length, actual);
        foreach (var property in expected)
        {
            Assert.IsTrue(actual.Contains(property), $"Missing file-object property {property}.");
        }
    }

    private static void AssertRuleSpecification(
        IReadOnlyCollection<ReadOnlyCreateRuleSpecification> specifications,
        ReadOnlyCreateRuleField field,
        EspIntegerComparisonType comparisonType,
        ulong value)
    {
        Assert.HasCount(
            1,
            specifications.Where(specification =>
                specification.Field == field &&
                specification.ComparisonType == comparisonType &&
                specification.Value == value));
    }
}
