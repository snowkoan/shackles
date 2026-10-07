using System.Windows;
using System.Windows.Controls;

namespace Shackles.App.Controls;

public partial class InstanceTabHeader : UserControl
{
    public InstanceTabHeader() => InitializeComponent();

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(InstanceTabHeader), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(string), typeof(InstanceTabHeader), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(InstanceTabHeader), new PropertyMetadata(string.Empty));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Status { get => (string)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
}
