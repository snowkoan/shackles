using System.Diagnostics;
using System.IO;

namespace Shackles.App.Infrastructure;

internal static class ElevatedApplicationLauncher
{
    internal static void Launch(string workspaceArgument)
    {
        var startInfo = CreateStartInfo(
            Environment.ProcessPath,
            AppContext.BaseDirectory,
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
        string? applicationDirectory,
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
            if (string.IsNullOrWhiteSpace(applicationDirectory))
            {
                throw new InvalidOperationException(
                    "Windows could not determine the Shackles application assembly path.");
            }

            startInfo.ArgumentList.Add(Path.Combine(applicationDirectory, "Shackles.dll"));
        }

        startInfo.ArgumentList.Add(workspaceArgument);
        return startInfo;
    }
}
