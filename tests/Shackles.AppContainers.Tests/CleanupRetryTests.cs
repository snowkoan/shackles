using Shackles.AppContainers.Internal;

namespace Shackles.AppContainers.Tests;

[TestClass]
public sealed class CleanupRetryTests
{
    [TestMethod]
    public void FailedBfsCleanupRetainsOwnershipAndRetriesRemainingResources()
    {
        var directory = Directory.CreateTempSubdirectory("Shackles-cleanup-").FullName;
        try
        {
            var configurator = new RetryConfigurator();
            using var manager = new AppContainerManager(directory, configurator);
            var identity = new AppContainerIdentity($"Shackles.{Guid.NewGuid():N}", "S-1-15-2-1", []);
            var journal = CleanupJournal.Create(directory, identity, "Retry cleanup");
            var profileDeletes = 0;
            using var sandbox = new AppContainerSandbox(identity, [], new AppContainerSandboxOptions
            {
                DisplayName = "Retry cleanup",
                FileSystemPolicyBackend = AppContainerFileSystemPolicyBackend.BrokeredFileSystem
            }, journal, configurator, _ => { profileDeletes++; return null; });
            sandbox.AddInitialGrant(TrackedAclGrant.From(new FileSystemGrant(directory, true, FileSystemGrantAccess.ReadExecute)));
            manager.TrackSandbox(sandbox);

            var first = manager.CloseAll().Single();
            Assert.IsFalse(first.Completed);
            Assert.IsTrue(sandbox.IsClosed);
            Assert.IsFalse(sandbox.CleanupCompleted);
            Assert.HasCount(1, manager.Sandboxes);
            Assert.IsTrue(File.Exists(journal.Path));
            Assert.AreEqual(0, profileDeletes, "A profile needed by pending BFS cleanup must remain.");
            Assert.ThrowsExactly<ObjectDisposedException>(() => sandbox.Launch(new AppContainerLaunchOptions("never-launched.exe")));

            var second = manager.CloseAll().Single();
            Assert.IsTrue(second.Completed, string.Join("; ", second.Warnings));
            Assert.IsTrue(sandbox.CleanupCompleted);
            Assert.IsEmpty(manager.Sandboxes);
            Assert.IsFalse(File.Exists(journal.Path));
            Assert.AreEqual(2, configurator.ClearAttempts);
            Assert.AreEqual(1, profileDeletes);
            Assert.IsTrue(sandbox.Close().Completed);
            Assert.AreEqual(2, configurator.ClearAttempts, "Successful cleanup remains idempotent.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void FailedJournalDeletionRetriesWithoutDeletingTheProfileAgain()
    {
        var directory = Directory.CreateTempSubdirectory("Shackles-journal-close-").FullName;
        try
        {
            var identity = new AppContainerIdentity($"Shackles.{Guid.NewGuid():N}", "S-1-15-2-1", []);
            var journal = CleanupJournal.Create(directory, identity, "Journal deletion retry");
            var profileDeletes = 0;
            using var sandbox = new AppContainerSandbox(identity, [], new AppContainerSandboxOptions
            {
                DisplayName = "Journal deletion retry"
            }, journal, new RetryConfigurator(), _ => { profileDeletes++; return null; });
            using (var locked = new FileStream(journal.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.IsFalse(sandbox.Close().Completed);
                Assert.AreEqual(1, profileDeletes);
            }

            Assert.IsTrue(sandbox.Close().Completed);
            Assert.AreEqual(1, profileDeletes, "Only the remaining journal should be retried.");
            Assert.IsFalse(File.Exists(journal.Path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void FailedGrantTrackingWriteIsPersistedBeforeARepeatedGrantIsAccepted()
    {
        var directory = Directory.CreateTempSubdirectory("Shackles-journal-grant-").FullName;
        try
        {
            var identity = new AppContainerIdentity($"Shackles.{Guid.NewGuid():N}", "S-1-15-2-1", []);
            var journal = CleanupJournal.Create(directory, identity, "Grant tracking retry");
            var grant = TrackedAclGrant.From(new FileSystemGrant(directory, true, FileSystemGrantAccess.ReadExecute));
            using (var locked = new FileStream(journal.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    journal.Track(grant);
                    Assert.Fail("A locked journal should reject the grant update.");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The failed write must not make memory claim that the intent was persisted.
                }
            }

            journal.Track(grant);
            var persisted = System.Text.Json.JsonSerializer.Deserialize<CleanupJournalRecord>(File.ReadAllText(journal.Path))!;
            Assert.AreEqual(grant.Key, persisted.Grants.Single().Key);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void FailedJournalUpdateCanBePersistedOnRetry()
    {
        var directory = Directory.CreateTempSubdirectory("Shackles-journal-").FullName;
        try
        {
            var identity = new AppContainerIdentity($"Shackles.{Guid.NewGuid():N}", "S-1-15-2-1", []);
            var journal = CleanupJournal.Create(directory, identity, "Journal retry");
            journal.MarkBrokeredFileSystemPolicyMayExist();
            using (var locked = new FileStream(journal.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    journal.MarkBrokeredFileSystemPolicyCleared();
                    Assert.Fail("A locked journal should reject the update.");
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Replacing a locked file can report either sharing or access denial on Windows.
                }
            }
            journal.MarkBrokeredFileSystemPolicyCleared();
            var persisted = System.Text.Json.JsonSerializer.Deserialize<CleanupJournalRecord>(File.ReadAllText(journal.Path))!;
            Assert.IsFalse(persisted.BrokeredFileSystemPolicyMayExist);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class RetryConfigurator : IBrokeredFileSystemConfigurator
    {
        public BrokeredFileSystemSupport Support { get; } = new(BrokeredFileSystemAvailability.Available,
            "Fake policy backend", new Version(10, 0), null, null, false, []);
        internal int ClearAttempts { get; private set; }
        public void AddPolicy(string appContainerName, TrackedAclGrant grant) { }
        public string? TryClearPolicy(string appContainerName) => ++ClearAttempts == 1 ? "Policy cleanup temporarily failed." : null;
    }
}
