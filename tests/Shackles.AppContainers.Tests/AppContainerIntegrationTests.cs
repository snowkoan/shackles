using System.Diagnostics;
using System.Text.Json;
using Shackles.AppContainers.Internal;
using Shackles.AppContainers.Interop;

namespace Shackles.AppContainers.Tests;

[TestClass]
[TestCategory("WindowsIntegration")]
[DoNotParallelize]
public sealed class AppContainerIntegrationTests
{
    [TestMethod]
    [Timeout(30000)]
    public void CreateLaunchTwiceAndCloseUsesOneSandboxAndCleansJournal()
    {
        var journalDirectory = CreateTemporaryDirectory();
        try
        {
            using var manager = new AppContainerManager(journalDirectory);
            var options = new AppContainerSandboxOptions
            {
                DisplayName = "Shackles integration sandbox",
                RestrictChildProcessCreation = true,
                UseMinimalEnvironment = true
            };
            var launchOptions = new AppContainerLaunchOptions(GetCommandPromptPath())
            {
                Arguments = "/d /c exit 0",
                IncludeTargetDirectoryGrant = false
            };

            var creation = manager.CreateAndLaunch(options, launchOptions);
            var sandbox = creation.Sandbox;
            Assert.IsGreaterThan(0, creation.FirstLaunch.ProcessId);
            StringAssert.StartsWith(sandbox.ProfileName, "Shackles.");
            StringAssert.StartsWith(sandbox.Sid, "S-1-15-2-");

            var second = sandbox.Launch(launchOptions);
            Assert.IsGreaterThan(0, second.ProcessId);
            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => sandbox.GetSnapshot().ProcessIds.Count == 0,
                    TimeSpan.FromSeconds(5)),
                "Both short-lived AppContainer processes should exit.");

            var cleanup = sandbox.Close();

            Assert.IsTrue(
                cleanup.Completed,
                string.Join(Environment.NewLine, cleanup.Warnings));
            Assert.IsTrue(sandbox.IsClosed);
            Assert.IsEmpty(Directory.EnumerateFiles(journalDirectory));
        }
        finally
        {
            Directory.Delete(journalDirectory, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public void LowPrivilegeLaunchCompletes()
    {
        var journalDirectory = CreateTemporaryDirectory();
        try
        {
            using var manager = new AppContainerManager(journalDirectory);
            var creation = manager.CreateAndLaunch(
                new AppContainerSandboxOptions
                {
                    DisplayName = "Shackles LPAC integration sandbox",
                    IsolationMode = AppContainerIsolationMode.LowPrivilege,
                    UseMinimalEnvironment = true
                },
                new AppContainerLaunchOptions(GetCommandPromptPath())
                {
                    Arguments = "/d /c exit 0",
                    IncludeTargetDirectoryGrant = false
                });

            Assert.IsGreaterThan(0, creation.FirstLaunch.ProcessId);
            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => creation.Sandbox.GetSnapshot().ProcessIds.Count == 0,
                    TimeSpan.FromSeconds(5)));
            var cleanup = creation.Sandbox.Close();
            Assert.IsTrue(
                cleanup.Completed,
                string.Join(Environment.NewLine, cleanup.Warnings));
        }
        finally
        {
            Directory.Delete(journalDirectory, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public void PrivateNetworkCapabilityResolvesAndLaunches()
    {
        var journalDirectory = CreateTemporaryDirectory();
        try
        {
            using var manager = new AppContainerManager(journalDirectory);
            var creation = manager.CreateAndLaunch(
                new AppContainerSandboxOptions
                {
                    DisplayName = "Shackles private network integration sandbox",
                    CapabilityNames = ["privateNetworkClientServer"]
                },
                new AppContainerLaunchOptions(GetCommandPromptPath())
                {
                    Arguments = "/d /c exit 0",
                    IncludeTargetDirectoryGrant = false
                });

            Assert.IsGreaterThan(0, creation.FirstLaunch.ProcessId);
            Assert.HasCount(1, creation.Sandbox.Options.CapabilityNames);
            Assert.AreEqual(
                "privateNetworkClientServer",
                creation.Sandbox.Options.CapabilityNames[0]);
            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => creation.Sandbox.GetSnapshot().ProcessIds.Count == 0,
                    TimeSpan.FromSeconds(5)));

            var cleanup = creation.Sandbox.Close();
            Assert.IsTrue(
                cleanup.Completed,
                string.Join(Environment.NewLine, cleanup.Warnings));
            Assert.IsEmpty(Directory.EnumerateFiles(journalDirectory));
        }
        finally
        {
            Directory.Delete(journalDirectory, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public void NetworkCredentialCapabilitiesResolveAndLaunch()
    {
        var journalDirectory = CreateTemporaryDirectory();
        try
        {
            using var manager = new AppContainerManager(journalDirectory);
            var creation = manager.CreateAndLaunch(
                new AppContainerSandboxOptions
                {
                    DisplayName = "Shackles network credentials integration sandbox",
                    CapabilityNames =
                    [
                        "enterpriseAuthentication",
                        "developmentModeNetwork"
                    ]
                },
                new AppContainerLaunchOptions(GetCommandPromptPath())
                {
                    Arguments = "/d /c exit 0",
                    IncludeTargetDirectoryGrant = false
                });

            Assert.IsGreaterThan(0, creation.FirstLaunch.ProcessId);
            Assert.HasCount(2, creation.Sandbox.Options.CapabilityNames);
            Assert.AreEqual(
                "enterpriseAuthentication",
                creation.Sandbox.Options.CapabilityNames[0]);
            Assert.AreEqual(
                "developmentModeNetwork",
                creation.Sandbox.Options.CapabilityNames[1]);
            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => creation.Sandbox.GetSnapshot().ProcessIds.Count == 0,
                    TimeSpan.FromSeconds(5)));

            var cleanup = creation.Sandbox.Close();
            Assert.IsTrue(
                cleanup.Completed,
                string.Join(Environment.NewLine, cleanup.Warnings));
            Assert.IsEmpty(Directory.EnumerateFiles(journalDirectory));
        }
        finally
        {
            Directory.Delete(journalDirectory, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public void FileAndRegistryGrantsAreAppliedAndRevoked()
    {
        var root = CreateTemporaryDirectory();
        var journalDirectory = Path.Combine(root, "journal");
        var grantedDirectory = Path.Combine(root, "granted");
        Directory.CreateDirectory(journalDirectory);
        Directory.CreateDirectory(grantedDirectory);
        var registrySubKey =
            $"Software\\Shackles.Tests\\{Guid.NewGuid():N}";
        using (Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registrySubKey))
        {
        }

        try
        {
            using var manager = new AppContainerManager(journalDirectory);
            var creation = manager.CreateAndLaunch(
                new AppContainerSandboxOptions
                {
                    DisplayName = "Shackles grant integration sandbox",
                    FileSystemGrants =
                    [
                        new FileSystemGrant(
                            grantedDirectory,
                            IsDirectory: true,
                            FileSystemGrantAccess.ReadWriteDelete)
                    ],
                    RegistryGrants =
                    [
                        new RegistryGrant(
                            $"HKCU\\{registrySubKey}",
                            RegistryGrantAccess.ReadWrite,
                            RegistryGrantView.Automatic)
                    ]
                },
                new AppContainerLaunchOptions(GetCommandPromptPath())
                {
                    Arguments = "/d /c exit 0",
                    IncludeTargetDirectoryGrant = false
                });
            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => creation.Sandbox.GetSnapshot().ProcessIds.Count == 0,
                    TimeSpan.FromSeconds(5)));

            var journalPath = Directory
                .EnumerateFiles(journalDirectory, "Shackles.*.json")
                .Single();
            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => TryReadJournal(journalPath, out var record) &&
                          record.Grants.Count == 0,
                    TimeSpan.FromSeconds(5)),
                "ACL cleanup intent should be removed after the final tracked process exits.");
            Assert.IsFalse(
                creation.Sandbox.IsClosed,
                "Idle resource cleanup should retain the reusable AppContainer profile.");

            var secondLaunch = creation.Sandbox.Launch(
                new AppContainerLaunchOptions(GetCommandPromptPath())
                {
                    Arguments = "/d /c exit 0",
                    IncludeTargetDirectoryGrant = false
                });
            Assert.IsGreaterThan(0, secondLaunch.ProcessId);
            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => creation.Sandbox.GetSnapshot().ProcessIds.Count == 0 &&
                          TryReadJournal(journalPath, out var record) &&
                          record.Grants.Count == 0,
                    TimeSpan.FromSeconds(5)),
                "A later launch should reapply and then release its configured ACL grants.");

            var cleanup = creation.Sandbox.Close();

            Assert.IsTrue(
                cleanup.Completed,
                string.Join(Environment.NewLine, cleanup.Warnings));
            Assert.IsEmpty(Directory.EnumerateFiles(journalDirectory));
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                registrySubKey,
                throwOnMissingSubKey: false);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public void BrokeredFileSystemBackendUsesInjectedConfiguratorAndJournal()
    {
        var root = CreateTemporaryDirectory();
        var journalDirectory = Path.Combine(root, "journal");
        var grantedDirectory = Path.Combine(root, "granted");
        Directory.CreateDirectory(journalDirectory);
        Directory.CreateDirectory(grantedDirectory);
        var fake = new FakeBrokeredFileSystemConfigurator();
        try
        {
            using var manager = new AppContainerManager(
                journalDirectory,
                fake);
            var creation = manager.CreateAndLaunch(
                new AppContainerSandboxOptions
                {
                    DisplayName = "Shackles BFS routing sandbox",
                    FileSystemPolicyBackend =
                        AppContainerFileSystemPolicyBackend.BrokeredFileSystem,
                    FileSystemGrants =
                    [
                        new FileSystemGrant(
                            grantedDirectory,
                            IsDirectory: true,
                            FileSystemGrantAccess.ReadWriteDelete)
                    ]
                },
                new AppContainerLaunchOptions(GetCommandPromptPath())
                {
                    Arguments = "/d /c choice /c y /d y /t 1 >nul",
                    IncludeTargetDirectoryGrant = false
                });

            Assert.HasCount(1, fake.AddedGrants);
            Assert.AreEqual(
                grantedDirectory,
                fake.AddedGrants[0].Target);
            Assert.IsTrue(creation.FirstLaunch.Warnings.Any(warning =>
                warning.Contains(
                    "experimental Brokered File System",
                    StringComparison.Ordinal)));
            var journalPath = Directory
                .EnumerateFiles(journalDirectory, "Shackles.*.json")
                .Single();
            var record = JsonSerializer.Deserialize<CleanupJournalRecord>(
                File.ReadAllText(journalPath))!;
            Assert.IsTrue(record.BrokeredFileSystemPolicyMayExist);
            Assert.IsEmpty(record.Grants);

            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => creation.Sandbox.GetSnapshot().ProcessIds.Count == 0,
                    TimeSpan.FromSeconds(5)));
            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => fake.ClearedAppContainerNames.Count == 1,
                    TimeSpan.FromSeconds(5)),
                "BFS policy should be cleared when the final tracked process exits.");
            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => TryReadJournal(journalPath, out record) &&
                          !record.BrokeredFileSystemPolicyMayExist,
                    TimeSpan.FromSeconds(5)),
                "The cleared BFS state should be persisted to the cleanup journal.");
            Assert.IsNotNull(record);
            Assert.IsFalse(record.BrokeredFileSystemPolicyMayExist);
            Assert.IsFalse(
                creation.Sandbox.IsClosed,
                "Idle BFS cleanup should retain the reusable AppContainer profile.");
            var cleanup = creation.Sandbox.Close();

            Assert.IsTrue(
                cleanup.Completed,
                string.Join(Environment.NewLine, cleanup.Warnings));
            Assert.HasCount(1, fake.ClearedAppContainerNames);
            Assert.AreEqual(
                creation.Sandbox.ProfileName,
                fake.ClearedAppContainerNames[0]);
            Assert.IsEmpty(Directory.EnumerateFiles(journalDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public void InboxEditStandardLaunchAvoidsLoaderInitializationFailure()
    {
        var editPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "edit.exe");
        if (!File.Exists(editPath))
        {
            Assert.Inconclusive("This Windows installation does not include inbox Microsoft Edit.");
        }

        var journalDirectory = CreateTemporaryDirectory();
        try
        {
            using var manager = new AppContainerManager(journalDirectory);
            var creation = manager.CreateAndLaunch(
                new AppContainerSandboxOptions
                {
                    DisplayName = "Shackles inbox Edit integration sandbox"
                },
                new AppContainerLaunchOptions(editPath)
                {
                    IncludeTargetDirectoryGrant = false
                });

            var processId = creation.FirstLaunch.ProcessId;
            Assert.IsGreaterThan(0, processId);
            Assert.IsTrue(
                SpinWait.SpinUntil(
                    () => creation.Sandbox.GetSnapshot().ProcessIds.Contains(processId),
                    TimeSpan.FromSeconds(1)),
                "The direct Edit launch should be tracked by its retained process handle.");

            using (var process = Process.GetProcessById(processId))
            {
                var processHandle = process.Handle;
                if (process.WaitForExit(2_000))
                {
                    if (NativeMethods.GetExitCodeProcessRaw(
                            processHandle,
                            out var exitCode) == 0)
                    {
                        Assert.Fail("Edit exited during initialization; its exit code could not be read.");
                    }

                    Assert.IsFalse(
                        exitCode == 0xC0000142,
                        "Edit reproduced the loader initialization failure (0xc0000142).");
                }
                else
                {
                    process.Refresh();
                    Assert.IsFalse(
                        process.MainWindowTitle.Contains(
                            "Application Error",
                            StringComparison.OrdinalIgnoreCase),
                        $"Edit displayed an initialization failure: {process.MainWindowTitle}");
                }
            }

            var cleanup = creation.Sandbox.Close();
            Assert.IsTrue(
                cleanup.Completed,
                string.Join(Environment.NewLine, cleanup.Warnings));
            Assert.IsEmpty(Directory.EnumerateFiles(journalDirectory));
        }
        finally
        {
            Directory.Delete(journalDirectory, recursive: true);
        }
    }

    private static string GetCommandPromptPath()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");
        Assert.IsTrue(File.Exists(path), $"Expected Windows command prompt at {path}.");
        return path;
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

    private static bool TryReadJournal(
        string path,
        out CleanupJournalRecord record)
    {
        try
        {
            record = JsonSerializer.Deserialize<CleanupJournalRecord>(
                File.ReadAllText(path))!;
            return record is not null;
        }
        catch (IOException)
        {
            record = null!;
            return false;
        }
        catch (JsonException)
        {
            record = null!;
            return false;
        }
    }

    private sealed class FakeBrokeredFileSystemConfigurator :
        IBrokeredFileSystemConfigurator
    {
        public BrokeredFileSystemSupport Support { get; } = new(
            BrokeredFileSystemAvailability.Available,
            "Fake BFS support for an integration test.",
            new Version(10, 0, 26100, 1),
            @"C:\Windows\System32\bfscfg.exe",
            @"C:\Windows\System32\drivers\bfs.sys",
            true,
            Array.Empty<string>());

        internal List<TrackedAclGrant> AddedGrants { get; } = [];

        internal List<string> ClearedAppContainerNames { get; } = [];

        public void AddPolicy(
            string appContainerName,
            TrackedAclGrant grant) => AddedGrants.Add(grant);

        public string? TryClearPolicy(string appContainerName)
        {
            ClearedAppContainerNames.Add(appContainerName);
            return null;
        }
    }
}
