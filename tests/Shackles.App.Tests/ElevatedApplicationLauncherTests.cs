using Shackles.App.Infrastructure;

namespace Shackles.App.Tests;

[TestClass]
public sealed class ElevatedApplicationLauncherTests
{
    [TestMethod]
    [DataRow(App.WespWorkspaceArgument)]
    [DataRow(App.WfpWorkspaceArgument)]
    public void ApplicationLaunchPreservesWorkspaceAndElevation(string workspaceArgument)
    {
        var startInfo = ElevatedApplicationLauncher.CreateStartInfo(
            @"C:\Program Files\Shackles\Shackles.exe", null, workspaceArgument);

        Assert.AreEqual(@"C:\Program Files\Shackles\Shackles.exe", startInfo.FileName);
        Assert.AreEqual(Environment.CurrentDirectory, startInfo.WorkingDirectory);
        Assert.IsTrue(startInfo.UseShellExecute);
        Assert.AreEqual("runas", startInfo.Verb);
        CollectionAssert.AreEqual(new[] { workspaceArgument }, startInfo.ArgumentList.ToArray());
    }

    [TestMethod]
    [DataRow("dotnet.exe")]
    [DataRow("DOTNET.EXE")]
    public void DotnetHostLaunchIncludesApplicationAssemblyBeforeWorkspace(string hostName)
    {
        const string assemblyPath = @"C:\Program Files\Shackles\Shackles.dll";
        var startInfo = ElevatedApplicationLauncher.CreateStartInfo(
            @"C:\dotnet\" + hostName, assemblyPath, App.WespWorkspaceArgument);

        CollectionAssert.AreEqual(
            new[] { assemblyPath, App.WespWorkspaceArgument }, startInfo.ArgumentList.ToArray());
    }

    [TestMethod]
    public void MissingExecutableOrDotnetApplicationPathRetainsExistingErrors()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ElevatedApplicationLauncher.CreateStartInfo(null, "Shackles.dll", App.WfpWorkspaceArgument));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ElevatedApplicationLauncher.CreateStartInfo("dotnet.exe", " ", App.WfpWorkspaceArgument));
    }
}
