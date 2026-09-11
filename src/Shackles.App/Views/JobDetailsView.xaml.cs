using System.Windows;
using System.Windows.Controls;

namespace Shackles.App.Views;

public partial class JobDetailsView : UserControl
{
    public JobDetailsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? AssignProcessesRequested;
    public event RoutedEventHandler? LaunchRequested;
    public event RoutedEventHandler? CloseRequested;

    private void AssignProcessesButton_Click(object sender, RoutedEventArgs e) =>
        AssignProcessesRequested?.Invoke(this, e);

    private void LaunchButton_Click(object sender, RoutedEventArgs e) => LaunchRequested?.Invoke(this, e);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);
}
