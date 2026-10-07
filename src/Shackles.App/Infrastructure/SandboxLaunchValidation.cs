using System.IO;
using System.Windows.Controls;

namespace Shackles.App.Infrastructure;

internal static class SandboxLaunchValidation
{
    internal static bool ValidatePaths(
        string executablePath,
        string workingDirectory,
        TextBox executableInput,
        TextBox workingDirectoryInput,
        Action<string> showNotice)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            showNotice("Choose an executable to launch.");
            executableInput.Focus();
            return false;
        }

        try
        {
            if (!File.Exists(Path.GetFullPath(executablePath)))
            {
                showNotice("The selected executable no longer exists.");
                executableInput.Focus();
                return false;
            }

            if (!string.IsNullOrWhiteSpace(workingDirectory) &&
                !Directory.Exists(Path.GetFullPath(workingDirectory)))
            {
                showNotice("The selected working directory no longer exists.");
                workingDirectoryInput.Focus();
                return false;
            }
        }
        catch (Exception exception)
        {
            showNotice($"The launch path is invalid: {exception.Message}");
            return false;
        }

        return true;
    }
}
