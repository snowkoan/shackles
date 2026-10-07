using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using Shackles.App.ViewModels;

namespace Shackles.App.Views;

public partial class JobDetailsView : UserControl
{
    private RestrictionEditorViewModel? _editor;

    public JobDetailsView()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (_editor is not null)
            {
                _editor.ValidationFailed -= EditorValidationFailed;
            }

            _editor = (args.NewValue as JobViewModel)?.Editor;
            if (_editor is not null)
            {
                _editor.ValidationFailed += EditorValidationFailed;
            }
        };
    }

    public event RoutedEventHandler? AssignProcessesRequested;
    public event RoutedEventHandler? LaunchRequested;
    public event RoutedEventHandler? CloseRequested;

    private void AssignProcessesButton_Click(object sender, RoutedEventArgs e) =>
        AssignProcessesRequested?.Invoke(this, e);

    private void LaunchButton_Click(object sender, RoutedEventArgs e) => LaunchRequested?.Invoke(this, e);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);

    private void DetailsChrome_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The measured chrome can grow with wrapped explanations and error details.
        // Reserve the tab strip and at least 120px of content rather than clipping it.
        MinHeight = Math.Ceiling(DetailsHeader.ActualHeight + DetailsFooter.ActualHeight + SettingsTabs.MinHeight);
    }

    private void EditorValidationFailed(object? sender, EventArgs args)
    {
        if (string.IsNullOrWhiteSpace(_editor?.ValidationPropertyName))
        {
            return;
        }

        var path = $"Editor.{_editor.ValidationPropertyName}";
        foreach (TabItem tab in SettingsTabs.Items)
        {
            if (tab.Content is not DependencyObject content || FindBoundControl(content, path) is not { } control)
            {
                continue;
            }

            SettingsTabs.SelectedItem = tab;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                control.BringIntoView();
                control.Focus();
                if (control is TextBox textBox)
                {
                    textBox.SelectAll();
                }
            });
            break;
        }
    }

    private static Control? FindBoundControl(DependencyObject element, string path)
    {
        if (element is Control control)
        {
            DependencyProperty[] properties = control switch
            {
                TextBox => [TextBox.TextProperty],
                ToggleButton => [ToggleButton.IsCheckedProperty],
                Selector => [Selector.SelectedItemProperty, Selector.SelectedValueProperty],
                _ => []
            };
            if (properties.Any(property =>
                BindingOperations.GetBinding(element, property)?.Path?.Path == path))
            {
                return control;
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            if (FindBoundControl(child, path) is { } match)
            {
                return match;
            }
        }

        return null;
    }
}
