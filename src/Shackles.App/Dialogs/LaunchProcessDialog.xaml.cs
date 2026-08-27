using System.IO;
using System.Windows;
using Shackles.App.Infrastructure;
using Shackles.App.Models;
using Microsoft.Win32;

namespace Shackles.App.Dialogs;

public partial class LaunchProcessDialog : Window
{
    internal LaunchProcessDialog(string jobName)
    {
        InitializeComponent();
        Title = $"Launch in {jobName}";
    }

    internal LaunchRequest? Request { get; private set; }

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose executable",
            CheckFileExists = true,
            Multiselect = false,
            Filter = "Windows executables (*.exe;*.com)|*.exe;*.com|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            ExecutableBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text))
            {
                WorkingDirectoryBox.Text = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            }
        }
    }

    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose working directory",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            WorkingDirectoryBox.Text = dialog.FolderName;
        }
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        var fileName = ExecutableBox.Text.Trim();
        var workingDirectory = WorkingDirectoryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(fileName) || !Path.IsPathFullyQualified(fileName))
        {
            ShowError("Choose an executable using an absolute path.");
            return;
        }

        if (!File.Exists(fileName))
        {
            ShowError("The selected executable no longer exists.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory) &&
            (!Path.IsPathFullyQualified(workingDirectory) || !Directory.Exists(workingDirectory)))
        {
            ShowError("The working directory must be an existing absolute path.");
            return;
        }

        try
        {
            Request = new LaunchRequest(
                Path.GetFullPath(fileName),
                WindowsCommandLine.ParseArguments(ArgumentsBox.Text),
                string.IsNullOrWhiteSpace(workingDirectory) ? null : Path.GetFullPath(workingDirectory));
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
            return;
        }

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
