using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespClientLibraryTests
{
    [TestMethod]
    public void ExplicitOverrideWinsWithoutProbingAppLocalLibrary()
    {
        var overridePath = Path.Combine(
            Path.GetTempPath(),
            "configured-wesp",
            NativeMethods.WespLibrary);
        var appLocalWasProbed = false;

        var candidate = WespClientLibrary.GetCandidatePath(
            overridePath,
            Path.Combine(Path.GetTempPath(), "app-local"),
            Environment.SystemDirectory,
            _ =>
            {
                appLocalWasProbed = true;
                return true;
            });

        Assert.AreEqual(Path.GetFullPath(overridePath), candidate);
        Assert.IsFalse(appLocalWasProbed);
    }

    [TestMethod]
    public void AppLocalLibraryWinsWhenItExists()
    {
        var appDirectory = Path.Combine(Path.GetTempPath(), "app-local");
        var appLocalPath = Path.Combine(appDirectory, NativeMethods.WespLibrary);

        var candidate = WespClientLibrary.GetCandidatePath(
            configured: null,
            appDirectory,
            Environment.SystemDirectory,
            path => StringComparer.OrdinalIgnoreCase.Equals(path, appLocalPath));

        Assert.AreEqual(appLocalPath, candidate);
    }

    [TestMethod]
    public void System32LibraryIsFallbackWhenAppLocalLibraryIsMissing()
    {
        var systemDirectory = Path.Combine(Path.GetTempPath(), "system32");

        var candidate = WespClientLibrary.GetCandidatePath(
            configured: "  ",
            Path.Combine(Path.GetTempPath(), "app-local"),
            systemDirectory,
            _ => false);

        Assert.AreEqual(
            Path.Combine(systemDirectory, NativeMethods.WespLibrary),
            candidate);
    }
}
