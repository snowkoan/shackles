using Shackles.AppContainers.Internal;
using System.Text.Json;

namespace Shackles.AppContainers.Tests;

[TestClass]
public sealed class AppContainerManagerTests
{
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
            Assert.HasCount(1, record.Grants);
            Assert.AreEqual(grantedDirectory, record.Grants[0].Target);
            File.WriteAllText(
                journal.Path,
                JsonSerializer.Serialize(
                    record with
                    {
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
