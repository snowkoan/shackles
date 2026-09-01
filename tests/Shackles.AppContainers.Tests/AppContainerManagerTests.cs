using Shackles.AppContainers.Internal;
using System.Text.Json;

namespace Shackles.AppContainers.Tests;

[TestClass]
public sealed class AppContainerManagerTests
{
    private static readonly string[] ExpectedBfsCapabilities =
    [
        "internetClient",
        AppContainerManager.BrokeredFileSystemCapabilityName
    ];

    private static readonly string[] ExpectedExistingBfsCapability =
        ["agenticappcontainer"];

    private static readonly string[] ExpectedAclCapabilities =
        ["internetClient"];

    [TestMethod]
    public void BfsBackendAddsTheDriverCapabilityToTheRuntimeToken()
    {
        var capabilities = AppContainerManager.BuildEffectiveCapabilityNames(
            new AppContainerSandboxOptions
            {
                DisplayName = "BFS capability sandbox",
                FileSystemPolicyBackend =
                    AppContainerFileSystemPolicyBackend.BrokeredFileSystem,
                CapabilityNames = ["internetClient"]
            });

        CollectionAssert.AreEqual(
            ExpectedBfsCapabilities,
            capabilities.ToArray());
    }

    [TestMethod]
    public void BfsBackendDoesNotDuplicateTheDriverCapability()
    {
        var capabilities = AppContainerManager.BuildEffectiveCapabilityNames(
            new AppContainerSandboxOptions
            {
                DisplayName = "BFS capability sandbox",
                FileSystemPolicyBackend =
                    AppContainerFileSystemPolicyBackend.BrokeredFileSystem,
                CapabilityNames = ["agenticappcontainer"]
            });

        CollectionAssert.AreEqual(
            ExpectedExistingBfsCapability,
            capabilities.ToArray());
    }

    [TestMethod]
    public void AclBackendDoesNotAddTheBfsDriverCapability()
    {
        var capabilities = AppContainerManager.BuildEffectiveCapabilityNames(
            new AppContainerSandboxOptions
            {
                DisplayName = "ACL capability sandbox",
                FileSystemPolicyBackend =
                    AppContainerFileSystemPolicyBackend.AccessControlLists,
                CapabilityNames = ["internetClient"]
            });

        CollectionAssert.AreEqual(
            ExpectedAclCapabilities,
            capabilities.ToArray());
    }

    [TestMethod]
    public void ConstructorReportsMalformedRecoveryJournal()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(
                    directory,
                    "Shackles.00000000000000000000000000000000.json"),
                "not json");

            using var manager = new AppContainerManager(directory);

            Assert.AreEqual(0, manager.RecoveryResult.RecoveredSessionCount);
            Assert.HasCount(1, manager.RecoveryResult.Warnings);
            StringAssert.Contains(
                manager.RecoveryResult.Warnings[0],
                "Could not read cleanup journal");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void RecoveryIgnoresJournalWithMismatchedSid()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var profileName = "Shackles.00000000000000000000000000000001";
            File.WriteAllText(
                Path.Combine(directory, $"{profileName}.json"),
                $$"""
                {
                  "FormatVersion": 1,
                  "OwnerProcessId": 0,
                  "OwnerCreationTimeFileTimeUtc": 0,
                  "DisplayName": "Untrusted",
                  "ProfileName": "{{profileName}}",
                  "Sid": "S-1-15-2-1",
                  "Grants": []
                }
                """);

            using var manager = new AppContainerManager(directory);

            Assert.AreEqual(0, manager.RecoveryResult.RecoveredSessionCount);
            Assert.HasCount(1, manager.RecoveryResult.Warnings);
            StringAssert.Contains(
                manager.RecoveryResult.Warnings[0],
                "stored SID does not match");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void RecoveryReportsJournalWithMissingRequiredFields()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(
                    directory,
                    "Shackles.00000000000000000000000000000002.json"),
                "{ \"FormatVersion\": 1 }");

            using var manager = new AppContainerManager(directory);

            Assert.AreEqual(0, manager.RecoveryResult.RecoveredSessionCount);
            Assert.HasCount(1, manager.RecoveryResult.Warnings);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void SelectedBfsBackendDoesNotFallBackWhenToolIsUnavailable()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var fake = new UnavailableBrokeredFileSystemConfigurator();
            using var manager = new AppContainerManager(directory, fake);

            var exception = Assert.ThrowsExactly<AppContainerException>(() =>
                manager.CreateAndLaunch(
                    new AppContainerSandboxOptions
                    {
                        DisplayName = "Unavailable BFS sandbox",
                        FileSystemPolicyBackend =
                            AppContainerFileSystemPolicyBackend.BrokeredFileSystem
                    },
                    new AppContainerLaunchOptions("not-launched.exe")
                    {
                        IncludeTargetDirectoryGrant = false
                    }));

            Assert.AreEqual(
                AppContainerOperation.ConfigureBrokeredFileSystem,
                exception.Operation);
            Assert.IsFalse(fake.AddPolicyCalled);
            Assert.IsEmpty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void StaleRecoveryRevokesJournaledGrantAndDeletesProfile()
    {
        var root = CreateTemporaryDirectory();
        var journalDirectory = Path.Combine(root, "journal");
        var grantedDirectory = Path.Combine(root, "granted");
        Directory.CreateDirectory(journalDirectory);
        Directory.CreateDirectory(grantedDirectory);
        var identity = AppContainerIdentity.Create("Recovery integration sandbox");
        var grant = AclGrantManager.Normalize(
            new FileSystemGrant(
                grantedDirectory,
                IsDirectory: true,
                FileSystemGrantAccess.ReadExecute));
        try
        {
            var journal = CleanupJournal.Create(
                journalDirectory,
                identity,
                "Recovery integration sandbox");
            journal.Track(grant);
            AclGrantManager.Apply(grant, identity.SidBytes);

            var record = JsonSerializer.Deserialize<CleanupJournalRecord>(
                File.ReadAllText(journal.Path))!;
            Assert.AreEqual(2, record.FormatVersion);
            Assert.HasCount(1, record.Grants);
            Assert.AreEqual(grantedDirectory, record.Grants[0].Target);
            File.WriteAllText(
                journal.Path,
                JsonSerializer.Serialize(
                    record with
                    {
                        FormatVersion = 1,
                        OwnerProcessId = 0,
                        OwnerCreationTimeFileTimeUtc = 0
                    }));

            using var manager = new AppContainerManager(journalDirectory);

            Assert.AreEqual(1, manager.RecoveryResult.RecoveredSessionCount);
            Assert.IsEmpty(manager.RecoveryResult.Warnings);
            Assert.IsEmpty(Directory.EnumerateFiles(journalDirectory));
        }
        finally
        {
            _ = AclGrantManager.TryRevoke(grant, identity.SidBytes);
            _ = AppContainerIdentity.TryDelete(identity.ProfileName);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void CleanupJournalUntrackPersistsTheRemainingIntent()
    {
        var root = CreateTemporaryDirectory();
        var journalDirectory = Path.Combine(root, "journal");
        var firstDirectory = Path.Combine(root, "first");
        var secondDirectory = Path.Combine(root, "second");
        Directory.CreateDirectory(journalDirectory);
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        var identity = AppContainerIdentity.Create("Journal untrack sandbox");
        try
        {
            var journal = CleanupJournal.Create(
                journalDirectory,
                identity,
                "Journal untrack sandbox");
            var first = AclGrantManager.Normalize(new FileSystemGrant(
                firstDirectory,
                IsDirectory: true,
                FileSystemGrantAccess.ReadExecute));
            var second = AclGrantManager.Normalize(new FileSystemGrant(
                secondDirectory,
                IsDirectory: true,
                FileSystemGrantAccess.ReadExecute));
            journal.Track(first);
            journal.Track(second);

            journal.Untrack(first);

            var record = JsonSerializer.Deserialize<CleanupJournalRecord>(
                File.ReadAllText(journal.Path))!;
            Assert.HasCount(1, record.Grants);
            Assert.AreEqual(second.Target, record.Grants[0].Target);
        }
        finally
        {
            _ = AppContainerIdentity.TryDelete(identity.ProfileName);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void StaleRecoveryClearsJournaledBfsPolicyWithoutRunningBfsCfg()
    {
        var root = CreateTemporaryDirectory();
        var journalDirectory = Path.Combine(root, "journal");
        Directory.CreateDirectory(journalDirectory);
        var identity = AppContainerIdentity.Create("BFS recovery sandbox");
        try
        {
            var journal = CleanupJournal.Create(
                journalDirectory,
                identity,
                "BFS recovery sandbox",
                AppContainerFileSystemPolicyBackend.BrokeredFileSystem);
            journal.MarkBrokeredFileSystemPolicyMayExist();
            MakeJournalStale(journal.Path);
            var fake = new FakeBrokeredFileSystemConfigurator();

            using var manager = new AppContainerManager(
                journalDirectory,
                fake);

            Assert.AreEqual(1, manager.RecoveryResult.RecoveredSessionCount);
            Assert.IsEmpty(manager.RecoveryResult.Warnings);
            Assert.HasCount(1, fake.ClearedAppContainerNames);
            Assert.AreEqual(
                identity.ProfileName,
                fake.ClearedAppContainerNames[0]);
            Assert.IsEmpty(Directory.EnumerateFiles(journalDirectory));
        }
        finally
        {
            _ = AppContainerIdentity.TryDelete(identity.ProfileName);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void StaleRecoveryRetainsProfileAndJournalWhenBfsClearFails()
    {
        var root = CreateTemporaryDirectory();
        var journalDirectory = Path.Combine(root, "journal");
        Directory.CreateDirectory(journalDirectory);
        var identity = AppContainerIdentity.Create("BFS retained sandbox");
        try
        {
            var journal = CleanupJournal.Create(
                journalDirectory,
                identity,
                "BFS retained sandbox",
                AppContainerFileSystemPolicyBackend.BrokeredFileSystem);
            journal.MarkBrokeredFileSystemPolicyMayExist();
            MakeJournalStale(journal.Path);
            var fake = new FakeBrokeredFileSystemConfigurator
            {
                ClearWarning = "Simulated BFS cleanup failure."
            };

            using var manager = new AppContainerManager(
                journalDirectory,
                fake);

            Assert.AreEqual(0, manager.RecoveryResult.RecoveredSessionCount);
            Assert.IsTrue(File.Exists(journal.Path));
            Assert.HasCount(1, fake.ClearedAppContainerNames);
            Assert.IsTrue(manager.RecoveryResult.Warnings.Any(warning =>
                warning.Contains(
                    "Simulated BFS cleanup failure",
                    StringComparison.Ordinal)));
            Assert.IsTrue(manager.RecoveryResult.Warnings.Any(warning =>
                warning.Contains("profile", StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("retained", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            _ = AppContainerIdentity.TryDelete(identity.ProfileName);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void MakeJournalStale(string path)
    {
        var record = JsonSerializer.Deserialize<CleanupJournalRecord>(
            File.ReadAllText(path))!;
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                record with
                {
                    OwnerProcessId = 0,
                    OwnerCreationTimeFileTimeUtc = 0
                }));
    }

    private sealed class FakeBrokeredFileSystemConfigurator :
        IBrokeredFileSystemConfigurator
    {
        public BrokeredFileSystemSupport Support { get; } = new(
            BrokeredFileSystemAvailability.Available,
            "Fake BFS support for a unit test.",
            new Version(10, 0, 26100, 1),
            @"C:\Windows\System32\bfscfg.exe",
            @"C:\Windows\System32\drivers\bfs.sys",
            true,
            Array.Empty<string>());

        internal string? ClearWarning { get; init; }

        internal List<string> ClearedAppContainerNames { get; } = [];

        public void AddPolicy(
            string appContainerName,
            TrackedAclGrant grant) =>
            throw new AssertFailedException(
                "Recovery must not add Brokered File System policy.");

        public string? TryClearPolicy(string appContainerName)
        {
            ClearedAppContainerNames.Add(appContainerName);
            return ClearWarning;
        }
    }

    private sealed class UnavailableBrokeredFileSystemConfigurator :
        IBrokeredFileSystemConfigurator
    {
        public BrokeredFileSystemSupport Support { get; } = new(
            BrokeredFileSystemAvailability.ConfigurationToolMissing,
            "bfscfg.exe is unavailable in this test.",
            new Version(10, 0, 19041, 1),
            null,
            null,
            false,
            Array.Empty<string>());

        internal bool AddPolicyCalled { get; private set; }

        public void AddPolicy(
            string appContainerName,
            TrackedAclGrant grant) => AddPolicyCalled = true;

        public string? TryClearPolicy(string appContainerName) =>
            throw new AssertFailedException(
                "No BFS policy exists to clear in this test.");
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "Shackles.AppContainers.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
