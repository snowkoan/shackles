using Shackles.AppContainers.Internal;

namespace Shackles.AppContainers.Tests;

[TestClass]
public sealed class BrokeredFileSystemConfiguratorTests
{
    private static readonly string[] ExpectedReadOnlyDirectoryArguments =
    [
        "--addpolicy",
        "--policybrokerreadonly",
        "--filename",
        @"C:\Workspace Folder",
        "--appid",
        "Shackles.0123456789abcdef0123456789abcdef",
        "--entrytype",
        "directory",
        "--containerinherit"
    ];

    private static readonly string[] ExpectedReadWriteFileArguments =
    [
        "--addpolicy",
        "--policybroker",
        "--filename",
        @"C:\Workspace\result.txt",
        "--appid",
        "Shackles.0123456789abcdef0123456789abcdef",
        "--entrytype",
        "file"
    ];

    private static readonly string[] ExpectedClearArguments =
    [
        "--clearpolicy",
        "--appid",
        "Shackles.0123456789abcdef0123456789abcdef"
    ];

    [TestMethod]
    public void ReadOnlyDirectoryArgumentsUseBrokerAndInheritance()
    {
        var grant = TrackedAclGrant.From(new FileSystemGrant(
            @"C:\Workspace Folder",
            IsDirectory: true,
            FileSystemGrantAccess.ReadExecute));

        var arguments = BrokeredFileSystemConfigurator.BuildAddArguments(
            "Shackles.0123456789abcdef0123456789abcdef",
            grant);

        CollectionAssert.AreEqual(
            ExpectedReadOnlyDirectoryArguments,
            arguments);
    }

    [TestMethod]
    public void ReadWriteFileArgumentsDoNotRequestInheritance()
    {
        var grant = TrackedAclGrant.From(new FileSystemGrant(
            @"C:\Workspace\result.txt",
            IsDirectory: false,
            FileSystemGrantAccess.ReadWriteDelete));

        var arguments = BrokeredFileSystemConfigurator.BuildAddArguments(
            "Shackles.0123456789abcdef0123456789abcdef",
            grant);

        CollectionAssert.AreEqual(
            ExpectedReadWriteFileArguments,
            arguments);
    }

    [TestMethod]
    public void DriveRootDoesNotRequestInheritance()
    {
        var grant = TrackedAclGrant.From(new FileSystemGrant(
            @"C:\",
            IsDirectory: true,
            FileSystemGrantAccess.ReadExecute));

        var arguments = BrokeredFileSystemConfigurator.BuildAddArguments(
            "Shackles.0123456789abcdef0123456789abcdef",
            grant);

        CollectionAssert.DoesNotContain(arguments, "--containerinherit");
    }

    [TestMethod]
    public void NonSystemDriveRootRequestsInheritanceLikeMxc()
    {
        var grant = TrackedAclGrant.From(new FileSystemGrant(
            @"D:\",
            IsDirectory: true,
            FileSystemGrantAccess.ReadExecute));

        var arguments = BrokeredFileSystemConfigurator.BuildAddArguments(
            "Shackles.0123456789abcdef0123456789abcdef",
            grant);

        CollectionAssert.Contains(arguments, "--containerinherit");
    }

    [TestMethod]
    public void ClearArgumentsAreScopedToTheAppContainerName()
    {
        var arguments = BrokeredFileSystemConfigurator.BuildClearArguments(
            "Shackles.0123456789abcdef0123456789abcdef");

        CollectionAssert.AreEqual(
            ExpectedClearArguments,
            arguments);
    }

    [TestMethod]
    public void ProbeReportsToolAndDriverWithoutExecutingEither()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var systemDirectory = Path.Combine(root, "System32");
            var driverDirectory = Path.Combine(systemDirectory, "drivers");
            Directory.CreateDirectory(driverDirectory);
            var toolPath = Path.Combine(systemDirectory, "bfscfg.exe");
            var driverPath = Path.Combine(driverDirectory, "bfs.sys");
            File.WriteAllBytes(toolPath, []);
            File.WriteAllBytes(driverPath, []);

            var support = BrokeredFileSystemSupportProbe.Probe(
                root,
                new Version(10, 0, 26100, 1),
                File.Exists);

            Assert.IsTrue(support.IsAvailable);
            Assert.AreEqual(
                BrokeredFileSystemAvailability.Available,
                support.Availability);
            Assert.AreEqual(toolPath, support.ConfigurationToolPath);
            Assert.AreEqual(driverPath, support.DriverPath);
            Assert.IsTrue(support.DriverFilePresent);
        }
        finally
        {
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
