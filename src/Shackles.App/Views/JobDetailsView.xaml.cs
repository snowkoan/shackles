using System.Windows;
using System.Windows.Controls;

namespace Shackles.App.Views;

public partial class JobDetailsView : UserControl
{
    public JobDetailsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? LaunchRequested;
    public event RoutedEventHandler? CloseRequested;

    private void LaunchButton_Click(object sender, RoutedEventArgs e) => LaunchRequested?.Invoke(this, e);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);
}
