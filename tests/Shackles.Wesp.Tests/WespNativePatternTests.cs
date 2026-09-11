using System.Runtime.InteropServices;
using Shackles.Wesp.Internal;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespNativePatternTests
{
    private static readonly string BundledClientPath = Path.Combine(
        AppContext.BaseDirectory,
        "espclient.dll");

    [TestMethod]
    public void GeneratedDirectoryPatternMatchesDirectAndDeepDescendantsOnly()
    {
        const string root = @"C:\Blocked";
        var descendantPattern = WespRuleCompiler
            .ExpandDirectoryPatterns(root)
            .Single(candidate => candidate.Value == root + @"\*")
            .Value;

        Assert.IsTrue(Matches(@"C:\Blocked\child.txt", descendantPattern));
        Assert.IsTrue(Matches(@"C:\Blocked\deep\child.txt", descendantPattern));
        Assert.IsTrue(Matches(@"C:\Blocked\child.txt:metadata", descendantPattern));
        Assert.IsFalse(Matches(root, descendantPattern));
        Assert.IsFalse(Matches(@"C:\BlockedSibling\child.txt", descendantPattern));
        Assert.IsFalse(Matches(@"C:\Block", descendantPattern));
    }

    [TestMethod]
    public void GeneratedDirectoryPatternsCoverNamedStreamsOnTheRoot()
    {
        const string root = @"C:\Blocked";
        var streamPattern = WespRuleCompiler
            .ExpandDirectoryPatterns(root)
            .Single(candidate => candidate.Value == root + ":*")
            .Value;

        Assert.IsTrue(Matches(root + ":metadata", streamPattern));
        Assert.IsTrue(Matches(root + ":metadata:$DATA", streamPattern));
        Assert.IsFalse(Matches(root, streamPattern));
        Assert.IsFalse(Matches(root + @"\child.txt:metadata", streamPattern));
        Assert.IsFalse(Matches(@"C:\BlockedSibling:metadata", streamPattern));
    }

    [TestMethod]
    public void GeneratedUncDevicePatternMatchesOnlyMupDescendants()
    {
        var descendantPattern = WespRuleCompiler
            .ExpandDirectoryPatterns(
                WespRuleCompiler.UncNtPathRoot,
                includeRootStreams: false)
            .Single(candidate => candidate.Pattern)
            .Value;

        Assert.IsTrue(Matches(@"\Device\Mup\server\share\file.txt", descendantPattern));
        Assert.IsTrue(Matches(@"\Device\Mup\server\share\deep\file.txt", descendantPattern));
        Assert.IsFalse(Matches(WespRuleCompiler.UncNtPathRoot, descendantPattern));
        Assert.IsFalse(Matches(@"\Device\MupSibling\server\share", descendantPattern));
    }

    [TestMethod]
    public void GeneratedRegistryPatternMatchesDescendantsWithoutPrefixCollisions()
    {
        const string root = @"\REGISTRY\MACHINE\Software\Shackles";
        var descendantPattern = WespRuleCompiler
            .ExpandRegistryPatterns(root)
            .Single(candidate => candidate.Pattern)
            .Value;

        Assert.IsTrue(Matches(root + @"\Value", descendantPattern));
        Assert.IsTrue(Matches(root + @"\Deep\Value", descendantPattern));
        Assert.IsFalse(Matches(root, descendantPattern));
        Assert.IsFalse(Matches(root + @"Sibling\Value", descendantPattern));
    }

    [TestMethod]
    public void GeneratedPatternPreservesLiteralWespMetacharacters()
    {
        const string root = @"\REGISTRY\MACHINE\Software\literal*question?pipe|star*{2}";
        var descendantPattern = WespRuleCompiler
            .ExpandRegistryPatterns(root)
            .Single(candidate => candidate.Pattern)
            .Value;

        Assert.IsTrue(Matches(root + @"\child", descendantPattern));
        Assert.IsFalse(Matches(
            @"\REGISTRY\MACHINE\Software\literalXquestionYpipeZstarAA\child",
            descendantPattern));
    }

    [TestMethod]
    public void BundledMatcherImplementsDocumentedSeparatorLimits()
    {
        Assert.IsTrue(Matches(@"a\b\c\d", @"a\*{2}\d"));
        Assert.IsTrue(Matches(@"a\b\c\c\d", @"a\*{2}\d"));
        Assert.IsFalse(Matches(@"a\b\c\c\c\d", @"a\*{2}\d"));
        Assert.IsTrue(Matches(@"a\b\d", @"a\*{0}\d"));
        Assert.IsFalse(Matches(@"a\b\c\d", @"a\*{0}\d"));
    }

    private static bool Matches(string value, string pattern)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            Assert.Inconclusive("The bundled WESP pattern matcher requires Windows x64.");
        }

        Assert.IsTrue(
            File.Exists(BundledClientPath),
            $"The bundled WESP client was not copied to the test output: {BundledClientPath}");
        try
        {
            return WespPatternMatcher.Matches(BundledClientPath, value, pattern);
        }
        catch (Exception exception)
        {
            Assert.Fail(
                $"The bundled WESP client could not run EspStringMatchesPattern: {exception.Message}");
            return false;
        }
    }
}
