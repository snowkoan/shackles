using System.Windows;
using System.Windows.Input;

namespace Shackles.App.Dialogs;

public partial class JobNameDialog : Window
{
    private readonly bool _openExisting;

    internal JobNameDialog(bool openExisting)
    {
        InitializeComponent();
        _openExisting = openExisting;
        Title = openExisting ? "Open named job" : "Create job";
        HeadingText.Text = Title;
        AcceptButton.Content = openExisting ? "_Open" : "_Create";
        NameHelpText.Text = openExisting
            ? @"Enter the exact Windows job-object name. Prefixes such as Global\ or Local\ are accepted by Windows when permitted."
            : "Optional. Leave blank for a private unnamed job held only by this app.";
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            Keyboard.Focus(NameBox);
        };
    }

    internal string? JobName { get; private set; }

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (_openExisting && string.IsNullOrWhiteSpace(name))
        {
            ShowError("Enter the exact name of the job to open.");
            return;
        }

        if (name.Contains('\0', StringComparison.Ordinal))
        {
            ShowError("A job name cannot contain a null character.");
            return;
        }

        JobName = string.IsNullOrWhiteSpace(name) ? null : name;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        NameBox.Focus();
    }
}
