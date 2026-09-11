using Shackles.Wesp.Internal;
using System.Security.Principal;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespPolicyTests
{
    private readonly List<string> _temporaryDirectories = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var directory in _temporaryDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void NormalizeHandlesAllPolicySettingsAndBlockedWinsExactConflicts()
    {
        var blockedDirectory = CreateTemporaryDirectory();
        var readOnlyDirectory = CreateTemporaryDirectory();
        var childImage = Path.Combine(blockedDirectory, "blocked.exe");

        var normalized = WespPolicyNormalizer.Normalize(new WespPolicy(
            BlockedFilePaths: [blockedDirectory, blockedDirectory.ToUpperInvariant()],
            ReadOnlyFilePaths: [blockedDirectory, readOnlyDirectory, readOnlyDirectory.ToUpperInvariant()],
            BlockedRegistryKeys:
            [
                @"HKLM\Software\Shackles.Tests\Blocked",
                @"hklm\software\shackles.tests\blocked"
            ],
            ReadOnlyRegistryKeys:
            [
                @"HKEY_LOCAL_MACHINE\Software\Shackles.Tests\Blocked",
                @"HKLM\Software\Shackles.Tests\ReadOnly"
            ],
            BlockedChildExecutables: [childImage, childImage.ToUpperInvariant()],
            BlockUncPaths: true));

        Assert.HasCount(1, normalized.BlockedFilePaths);
        Assert.HasCount(1, normalized.ReadOnlyFilePaths);
        Assert.HasCount(1, normalized.BlockedRegistryKeys);
        Assert.HasCount(1, normalized.ReadOnlyRegistryKeys);
        Assert.HasCount(1, normalized.BlockedChildExecutables);
        Assert.IsTrue(normalized.BlockUncPaths);
        Assert.IsTrue(Path.IsPathFullyQualified(normalized.BlockedFilePaths[0]));
        Assert.AreEqual(readOnlyDirectory, normalized.ReadOnlyFilePaths[0]);
        Assert.AreEqual(
            @"\REGISTRY\MACHINE\Software\Shackles.Tests\Blocked",
            normalized.BlockedRegistryKeys[0],
            ignoreCase: true);
        Assert.AreEqual(
            @"\REGISTRY\MACHINE\Software\Shackles.Tests\ReadOnly",
            normalized.ReadOnlyRegistryKeys[0],
            ignoreCase: true);
    }

    [TestMethod]
    public void NormalizeRegistryAliasesToCanonicalNtPathsWithoutRequiringKeysToExist()
    {
        var uniqueSubKey = $@"Software\Shackles.Tests\Missing-{Guid.NewGuid():N}";

        var normalized = WespPolicyNormalizer.Normalize(EmptyPolicy() with
        {
            BlockedRegistryKeys =
            [
                $@"Computer\HKLM\{uniqueSubKey}",
                $@"HKCU\{uniqueSubKey}",
                $@"HKU\S-1-5-18\{uniqueSubKey}"
            ]
        });

        Assert.HasCount(3, normalized.BlockedRegistryKeys);
        Assert.IsTrue(normalized.BlockedRegistryKeys.Contains(
            $@"\REGISTRY\MACHINE\{uniqueSubKey}",
            StringComparer.OrdinalIgnoreCase));
        Assert.IsTrue(normalized.BlockedRegistryKeys.Any(path =>
            path.StartsWith(@"\REGISTRY\USER\S-", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(uniqueSubKey, StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(normalized.BlockedRegistryKeys.Contains(
            $@"\REGISTRY\USER\S-1-5-18\{uniqueSubKey}",
            StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void NormalizeHkcrExpandsToUserAndMachineClassViews()
    {
        var normalized = WespPolicyNormalizer.Normalize(EmptyPolicy() with
        {
            ReadOnlyRegistryKeys = [@"HKCR\Shackles.Tests"]
        });

        Assert.HasCount(2, normalized.ReadOnlyRegistryKeys);
        Assert.IsTrue(normalized.ReadOnlyRegistryKeys.Any(path =>
            path.StartsWith(@"\REGISTRY\USER\S-", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(@"_Classes\Shackles.Tests", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(normalized.ReadOnlyRegistryKeys.Contains(
            @"\REGISTRY\MACHINE\Software\Classes\Shackles.Tests",
            StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void NormalizeResolvesCurrentUserClassesToItsNativeMount()
    {
        var normalized = WespPolicyNormalizer.Normalize(EmptyPolicy() with
        {
            BlockedRegistryKeys = [@"HKCU\Software\Classes\Shackles.Tests"]
        });

        Assert.HasCount(1, normalized.BlockedRegistryKeys);
        Assert.IsTrue(normalized.BlockedRegistryKeys[0].StartsWith(
            @"\REGISTRY\USER\S-",
            StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(normalized.BlockedRegistryKeys[0].EndsWith(
            @"_Classes\Shackles.Tests",
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void NormalizePreservesSupportedCanonicalRegistryPaths()
    {
        const string canonicalPath = @"\REGISTRY\MACHINE\Software\Shackles.Tests";

        var normalized = WespPolicyNormalizer.Normalize(EmptyPolicy() with
        {
            BlockedRegistryKeys = [canonicalPath]
        });

        Assert.HasCount(1, normalized.BlockedRegistryKeys);
        Assert.AreEqual(canonicalPath, normalized.BlockedRegistryKeys[0]);
    }

    [TestMethod]
    public void NormalizeAcceptsRegistryHiveRootsWithoutSubkeys()
    {
        var normalized = WespPolicyNormalizer.Normalize(EmptyPolicy() with
        {
            BlockedRegistryKeys = ["HKLM"],
            ReadOnlyRegistryKeys = ["HKCU"]
        });

        Assert.AreEqual(@"\REGISTRY\MACHINE", normalized.BlockedRegistryKeys[0]);
        Assert.IsTrue(normalized.ReadOnlyRegistryKeys[0].StartsWith(
            @"\REGISTRY\USER\S-",
            StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void NormalizeHkcuRootExpandsToUserAndUserClassesHives()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value;
        Assert.IsNotNull(sid);

        var normalized = WespPolicyNormalizer.Normalize(EmptyPolicy() with
        {
            BlockedRegistryKeys = ["HKCU"]
        });

        Assert.HasCount(2, normalized.BlockedRegistryKeys);
        Assert.IsTrue(normalized.BlockedRegistryKeys.Contains(
            $@"\REGISTRY\USER\{sid}",
            StringComparer.OrdinalIgnoreCase));
        Assert.IsTrue(normalized.BlockedRegistryKeys.Contains(
            $@"\REGISTRY\USER\{sid}_Classes",
            StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void NormalizeHkuSidRootExpandsToUserAndUserClassesHives()
    {
        const string sid = "S-1-5-21-1000000000-2000000000-3000000000-1001";

        var normalized = WespPolicyNormalizer.Normalize(EmptyPolicy() with
        {
            ReadOnlyRegistryKeys = [$@"HKU\{sid}"]
        });

        Assert.HasCount(2, normalized.ReadOnlyRegistryKeys);
        Assert.IsTrue(normalized.ReadOnlyRegistryKeys.Contains(
            $@"\REGISTRY\USER\{sid}",
            StringComparer.OrdinalIgnoreCase));
        Assert.IsTrue(normalized.ReadOnlyRegistryKeys.Contains(
            $@"\REGISTRY\USER\{sid}_Classes",
            StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void NormalizeCanonicalUserClassesPathIsIdempotent()
    {
        const string canonicalPath =
            @"\REGISTRY\USER\S-1-5-21-1000000000-2000000000-3000000000-1001_Classes\Software\Classes\X";

        var first = WespPolicyNormalizer.Normalize(EmptyPolicy() with
        {
            BlockedRegistryKeys = [canonicalPath]
        });
        var second = WespPolicyNormalizer.Normalize(EmptyPolicy() with
        {
            BlockedRegistryKeys = first.BlockedRegistryKeys
        });

        Assert.HasCount(1, first.BlockedRegistryKeys);
        Assert.AreEqual(canonicalPath, first.BlockedRegistryKeys[0]);
        Assert.HasCount(1, second.BlockedRegistryKeys);
        Assert.AreEqual(canonicalPath, second.BlockedRegistryKeys[0]);
    }

    [TestMethod]
    [DataRow(@"HKCC\Software\Shackles.Tests")]
    [DataRow(@"HKLM\Software\\Shackles.Tests")]
    [DataRow(@"\REGISTRY\OTHER\Shackles.Tests")]
    [DataRow(@"UNKNOWN\Shackles.Tests")]
    public void NormalizeRejectsUnsupportedOrAmbiguousRegistryPaths(string registryPath)
    {
        var exception = Assert.ThrowsExactly<WespException>(() =>
            WespPolicyNormalizer.Normalize(EmptyPolicy() with
            {
                BlockedRegistryKeys = [registryPath]
            }));

        Assert.AreEqual(WespOperation.ValidatePolicy, exception.Operation);
    }

    [TestMethod]
    public void NormalizeRejectsMissingProtectedFolder()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var exception = Assert.ThrowsExactly<WespException>(() =>
            WespPolicyNormalizer.Normalize(EmptyPolicy() with
            {
                BlockedFilePaths = [missing]
            }));

        Assert.AreEqual(WespOperation.ValidatePolicy, exception.Operation);
    }

    [TestMethod]
    public void NormalizeRejectsBlockedChildPathWithoutAFileName()
    {
        var exception = Assert.ThrowsExactly<WespException>(() =>
            WespPolicyNormalizer.Normalize(EmptyPolicy() with
            {
                BlockedChildExecutables = [Path.GetPathRoot(Environment.SystemDirectory)!]
            }));

        Assert.AreEqual(WespOperation.ValidatePolicy, exception.Operation);
        StringAssert.Contains(exception.Message, "must include a file name");
    }

    [TestMethod]
    [DataRow("plain", "\"plain\"")]
    [DataRow("ends-with\\", "\"ends-with\\\\\"")]
    [DataRow("has \"quote\"", "\"has \\\"quote\\\"\"")]
    public void QuoteArgumentUsesWindowsCommandLineRules(string input, string expected)
    {
        Assert.AreEqual(expected, WespProcessLauncher.QuoteArgument(input));
    }

    private string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "Shackles.Wesp.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _temporaryDirectories.Add(directory);
        return directory;
    }

    private static WespPolicy EmptyPolicy() => new(
        BlockedFilePaths: [],
        ReadOnlyFilePaths: [],
        BlockedRegistryKeys: [],
        ReadOnlyRegistryKeys: [],
        BlockedChildExecutables: [],
        BlockUncPaths: false);
}
