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
    [DataRow("dotnet.exe", @"C:\Program Files\Shackles")]
    [DataRow("DOTNET.EXE", @"C:\Program Files\Shackles\")]
    public void DotnetHostLaunchIncludesApplicationAssemblyBeforeWorkspace(string hostName, string applicationDirectory)
    {
        const string assemblyPath = @"C:\Program Files\Shackles\Shackles.dll";
        var startInfo = ElevatedApplicationLauncher.CreateStartInfo(
            @"C:\dotnet\" + hostName, applicationDirectory, App.WespWorkspaceArgument);

        CollectionAssert.AreEqual(
            new[] { assemblyPath, App.WespWorkspaceArgument }, startInfo.ArgumentList.ToArray());
    }

    [TestMethod]
    public void DotnetHostLaunchUsesRuntimeBaseDirectory()
    {
        var startInfo = ElevatedApplicationLauncher.CreateStartInfo(
            "dotnet.exe", AppContext.BaseDirectory, App.WfpWorkspaceArgument);

        Assert.AreEqual(System.IO.Path.Combine(AppContext.BaseDirectory, "Shackles.dll"), startInfo.ArgumentList[0]);
        Assert.AreEqual(Environment.CurrentDirectory, startInfo.WorkingDirectory);
        Assert.AreEqual(App.WfpWorkspaceArgument, startInfo.ArgumentList[1]);
    }

    [TestMethod]
    public void MissingExecutableOrDotnetApplicationPathRetainsExistingErrors()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ElevatedApplicationLauncher.CreateStartInfo(null, AppContext.BaseDirectory, App.WfpWorkspaceArgument));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ElevatedApplicationLauncher.CreateStartInfo("dotnet.exe", " ", App.WfpWorkspaceArgument));
    }
}
