using System.IO;
using System.Windows.Controls;
using Shackles.App.Infrastructure;
using Shackles.App.Models;

namespace Shackles.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SandboxLaunchValidationTests
{
    [TestMethod]
    public async Task ValidCapturedPathsAndOptionalWorkingDirectoryDoNotShowANotice()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(() =>
        {
            var executable = typeof(SandboxLaunchValidation).Assembly.Location;
            var workingDirectory = Path.GetDirectoryName(executable)!;
            var notice = string.Empty;
            var executableInput = new TextBox { Text = "different editor contents" };
            var workingDirectoryInput = new TextBox { Text = "different editor contents" };

            Assert.IsTrue(SandboxLaunchValidation.ValidatePaths(executable, workingDirectory,
                executableInput, workingDirectoryInput, message => notice = message));
            Assert.IsTrue(SandboxLaunchValidation.ValidatePaths(executable, string.Empty,
                executableInput, workingDirectoryInput, message => notice = message));
            Assert.IsTrue(SandboxLaunchValidation.ValidatePaths(
                Path.GetRelativePath(Environment.CurrentDirectory, executable), workingDirectory,
                executableInput, workingDirectoryInput, message => notice = message));
            Assert.AreEqual(string.Empty, notice);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task EmptyExecutableKeepsTheExistingNotice(string executable)
    {
        await AssertInvalidPaths(executable, string.Empty, "Choose an executable to launch.");
    }

    [TestMethod]
    public async Task MissingExecutableKeepsTheExistingNotice()
    {
        await AssertInvalidPaths(MissingPath(), string.Empty, "The selected executable no longer exists.");
    }

    [TestMethod]
    public async Task MissingWorkingDirectoryKeepsTheExistingNotice()
    {
        await AssertInvalidPaths(typeof(SandboxLaunchValidation).Assembly.Location, MissingPath(),
            "The selected working directory no longer exists.");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task MalformedPathsKeepTheExistingNoticePrefix(bool executableIsMalformed)
    {
        await SandboxWorkspaceLifecycleTests.OnUi(() =>
        {
            var notice = string.Empty;
            Assert.IsFalse(SandboxLaunchValidation.ValidatePaths(
                executableIsMalformed ? "\0" : typeof(SandboxLaunchValidation).Assembly.Location,
                executableIsMalformed ? string.Empty : "\0",
                new TextBox(), new TextBox(), message => notice = message));
            StringAssert.StartsWith(notice, "The launch path is invalid: ");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void EditableRuleLabelsNotifyOnlyWhenTheirValuesChange()
    {
        var registry = new AppContainerRegistryGrantDraft("HKCU\\Software\\Example", 0, 0);
        var registryNotices = new List<string?>();
        registry.PropertyChanged += (_, args) => registryNotices.Add(args.PropertyName);
        registry.AccessIndex = 0;
        registry.AccessIndex = 1;
        registry.AccessIndex = 1;
        registry.ViewIndex = 1;
        registry.ViewIndex = 1;
        Assert.HasCount(2, registryNotices);
        Assert.IsTrue(registryNotices.All(name => name == nameof(registry.Summary)));
        StringAssert.Contains(registry.Summary, "Read and write");
        StringAssert.Contains(registry.Summary, "32-bit view");

        var rule = new ExperimentalSandboxFileRuleDraft("C:\\Example", 0);
        var ruleNotices = new List<string?>();
        rule.PropertyChanged += (_, args) => ruleNotices.Add(args.PropertyName);
        rule.AccessIndex = 0;
        rule.AccessIndex = 1;
        rule.AccessIndex = 1;
        Assert.HasCount(1, ruleNotices);
        Assert.AreEqual(nameof(rule.AccessSummary), ruleNotices.Single());
        Assert.AreEqual("Read only", rule.AccessSummary);
    }

    private static string MissingPath() => Path.Combine(Path.GetTempPath(), $"Shackles-missing-{Guid.NewGuid():N}");

    private static Task AssertInvalidPaths(string executable, string workingDirectory, string expectedNotice) =>
        SandboxWorkspaceLifecycleTests.OnUi(() =>
        {
            var notice = string.Empty;
            Assert.IsFalse(SandboxLaunchValidation.ValidatePaths(executable, workingDirectory,
                new TextBox(), new TextBox(), message => notice = message));
            Assert.AreEqual(expectedNotice, notice);
            return Task.CompletedTask;
        });
}
