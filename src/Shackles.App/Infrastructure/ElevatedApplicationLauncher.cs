using System.Diagnostics;
using System.IO;

namespace Shackles.App.Infrastructure;

internal static class ElevatedApplicationLauncher
{
    internal static void Launch(string workspaceArgument)
    {
        var startInfo = CreateStartInfo(
            Environment.ProcessPath,
            typeof(App).Assembly.Location,
            workspaceArgument);
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException(
                "Windows did not start the administrator copy of Shackles.");
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string? executablePath,
        string? applicationAssemblyPath,
        string workspaceArgument)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException(
                "Windows could not determine the Shackles application path.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = true,
            Verb = "runas"
        };
        if (string.Equals(
                Path.GetFileNameWithoutExtension(executablePath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(applicationAssemblyPath))
            {
                throw new InvalidOperationException(
                    "Windows could not determine the Shackles application assembly path.");
            }

            startInfo.ArgumentList.Add(applicationAssemblyPath);
        }

        startInfo.ArgumentList.Add(workspaceArgument);
        return startInfo;
    }
}
