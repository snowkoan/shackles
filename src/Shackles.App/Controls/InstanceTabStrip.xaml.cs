using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace Shackles.App.Controls;

public partial class InstanceTabStrip : ListBox
{
    private ScrollViewer? _scroller;
    private bool _revealScheduled;

    public InstanceTabStrip() => InitializeComponent();

    public static readonly DependencyProperty IsNewEnabledProperty = DependencyProperty.Register(
        nameof(IsNewEnabled), typeof(bool), typeof(InstanceTabStrip), new PropertyMetadata(true));
    public static readonly DependencyProperty NewToolTipProperty = DependencyProperty.Register(
        nameof(NewToolTip), typeof(object), typeof(InstanceTabStrip), new PropertyMetadata("Create a new instance"));
    public static readonly DependencyProperty CloseEnabledMemberPathProperty = DependencyProperty.Register(
        nameof(CloseEnabledMemberPath), typeof(string), typeof(InstanceTabStrip), new PropertyMetadata(string.Empty, ContainerBindingChanged));
    public static readonly DependencyProperty IsCloseEnabledProperty = DependencyProperty.RegisterAttached(
        "IsCloseEnabled", typeof(bool), typeof(InstanceTabStrip), new PropertyMetadata(true));
    private static readonly DependencyPropertyKey HasOverflowPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasOverflow), typeof(bool), typeof(InstanceTabStrip), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey CanScrollLeftPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CanScrollLeft), typeof(bool), typeof(InstanceTabStrip), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey CanScrollRightPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CanScrollRight), typeof(bool), typeof(InstanceTabStrip), new PropertyMetadata(false));
    public static readonly DependencyProperty HasOverflowProperty = HasOverflowPropertyKey.DependencyProperty;
    public static readonly DependencyProperty CanScrollLeftProperty = CanScrollLeftPropertyKey.DependencyProperty;
    public static readonly DependencyProperty CanScrollRightProperty = CanScrollRightPropertyKey.DependencyProperty;
    public static readonly RoutedEvent NewRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(NewRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(InstanceTabStrip));

    public bool IsNewEnabled { get => (bool)GetValue(IsNewEnabledProperty); set => SetValue(IsNewEnabledProperty, value); }
    public object NewToolTip { get => GetValue(NewToolTipProperty); set => SetValue(NewToolTipProperty, value); }
    public string CloseEnabledMemberPath { get => (string)GetValue(CloseEnabledMemberPathProperty); set => SetValue(CloseEnabledMemberPathProperty, value); }
    public bool HasOverflow => (bool)GetValue(HasOverflowProperty);
    public bool CanScrollLeft => (bool)GetValue(CanScrollLeftProperty);
    public bool CanScrollRight => (bool)GetValue(CanScrollRightProperty);
    public static bool GetIsCloseEnabled(DependencyObject item) => (bool)item.GetValue(IsCloseEnabledProperty);
    public static void SetIsCloseEnabled(DependencyObject item, bool value) => item.SetValue(IsCloseEnabledProperty, value);

    public event RoutedEventHandler NewRequested
    {
        add => AddHandler(NewRequestedEvent, value);
        remove => RemoveHandler(NewRequestedEvent, value);
    }
    public event EventHandler<InstanceTabCloseRequestedEventArgs>? CloseRequested;

    public override void OnApplyTemplate()
    {
        if (_scroller is not null)
        {
            _scroller.ScrollChanged -= ScrollerScrollChanged;
        }
        base.OnApplyTemplate();
        _scroller = GetTemplateChild("PART_TabScroller") as ScrollViewer;
        if (_scroller is not null)
        {
            _scroller.ScrollChanged += ScrollerScrollChanged;
        }
        UpdateOverflow();
        ScheduleSelectedIntoView();
    }

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        BindContainer(element, item);
    }

    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        BindingOperations.ClearBinding(element, AutomationProperties.NameProperty);
        BindingOperations.ClearBinding(element, IsCloseEnabledProperty);
        base.ClearContainerForItemOverride(element, item);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        ScheduleSelectedIntoView();
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        ScheduleSelectedIntoView();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && (HandleWorkspaceKeyDown(e) || HandleNavigationKey(e.Key, Keyboard.Modifiers, fromWorkspace: false)))
        {
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    /// <summary>Call from the containing workspace's PreviewKeyDown to cycle from its editor.</summary>
    public bool HandleWorkspaceKeyDown(KeyEventArgs args)
    {
        if (args.Handled || !HandleNavigationKey(args.Key, Keyboard.Modifiers, fromWorkspace: true))
        {
            return false;
        }
        args.Handled = true;
        return true;
    }

    internal bool HandleNavigationKey(Key key, ModifierKeys modifiers, bool fromWorkspace)
    {
        if (!IsEnabled || Items.Count == 0)
        {
            return false;
        }
        if (key == Key.Tab && (modifiers == ModifierKeys.Control || modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            var backwards = (modifiers & ModifierKeys.Shift) != 0;
            var index = SelectedIndex < 0 ? (backwards ? 0 : -1) : SelectedIndex;
            SelectAndFocus((index + (backwards ? -1 : 1) + Items.Count) % Items.Count, focus: !fromWorkspace);
            return true;
        }
        if (fromWorkspace || modifiers != ModifierKeys.None)
        {
            return false;
        }
        switch (key)
        {
            case Key.Left: SelectAndFocus(Math.Max(0, SelectedIndex - 1), focus: true); return true;
            case Key.Right: SelectAndFocus(Math.Min(Items.Count - 1, SelectedIndex + 1), focus: true); return true;
            case Key.Home: SelectAndFocus(0, focus: true); return true;
            case Key.End: SelectAndFocus(Items.Count - 1, focus: true); return true;
            case Key.Delete: return SelectedItem is not null && RequestClose(SelectedItem);
            default: return false;
        }
    }

    internal bool RequestClose(object item)
    {
        if (!IsEnabled || !Items.Contains(item))
        {
            return false;
        }
        var container = ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
        if (container is null)
        {
            UpdateLayout();
            container = ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
        }
        if (container is null || !GetIsCloseEnabled(container))
        {
            return false;
        }
        CloseRequested?.Invoke(this, new InstanceTabCloseRequestedEventArgs(item));
        return true;
    }

    private static void ContainerBindingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var strip = (InstanceTabStrip)sender;
        foreach (var item in strip.Items)
        {
            if (strip.ItemContainerGenerator.ContainerFromItem(item) is { } container)
            {
                strip.BindContainer(container, item);
            }
        }
    }

    private void BindContainer(DependencyObject container, object item)
    {
        BindingOperations.SetBinding(container, AutomationProperties.NameProperty,
            new Binding("DisplayName") { Source = item, Mode = BindingMode.OneWay, FallbackValue = item.ToString() });
        AutomationProperties.SetHelpText(container, "Use Left and Right to select an instance, or Control+Tab from its editor. Delete closes the selected instance.");
        BindingOperations.ClearBinding(container, IsCloseEnabledProperty);
        if (!string.IsNullOrWhiteSpace(CloseEnabledMemberPath))
        {
            BindingOperations.SetBinding(container, IsCloseEnabledProperty,
                new Binding(CloseEnabledMemberPath) { Source = item, Mode = BindingMode.OneWay, FallbackValue = false });
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button { TemplatedParent: ListBoxItem container })
        {
            var item = ItemContainerGenerator.ItemFromContainer(container);
            if (item != DependencyProperty.UnsetValue)
            {
                RequestClose(item);
            }
        }
        args.Handled = true;
    }

    private void NewButton_Click(object sender, RoutedEventArgs args)
    {
        if (IsEnabled && IsNewEnabled)
        {
            RaiseEvent(new RoutedEventArgs(NewRequestedEvent, this));
        }
        args.Handled = true;
    }

    private void ScrollLeftButton_Click(object sender, RoutedEventArgs args) => ScrollBy(-1);
    private void ScrollRightButton_Click(object sender, RoutedEventArgs args) => ScrollBy(1);
    private void ScrollBy(int direction) => _scroller?.ScrollToHorizontalOffset(
        _scroller.HorizontalOffset + direction * Math.Max(100, _scroller.ViewportWidth * 0.7));
    private void ScrollerScrollChanged(object sender, ScrollChangedEventArgs args)
    {
        UpdateOverflow();
        if (args.ExtentWidthChange != 0 || args.ViewportWidthChange != 0)
        {
            ScheduleSelectedIntoView();
        }
    }

    private void UpdateOverflow()
    {
        // Compare against room available without arrows, so they disappear when
        // resizing or closing tabs makes the row fit again.
        var availableWidth = ActualWidth;
        if (GetTemplateChild("PART_NewButton") is FrameworkElement newButton)
        {
            availableWidth -= newButton.ActualWidth + 5;
        }
        var overflow = _scroller is not null && _scroller.ExtentWidth > Math.Max(0, availableWidth) + 0.5;
        SetValue(HasOverflowPropertyKey, overflow);
        SetValue(CanScrollLeftPropertyKey, overflow && _scroller is { HorizontalOffset: > 0.5 });
        SetValue(CanScrollRightPropertyKey, overflow && _scroller is not null && _scroller.ScrollableWidth - _scroller.HorizontalOffset > 0.5);
    }

    private void SelectAndFocus(int index, bool focus)
    {
        SelectedIndex = index;
        ScrollIntoView(SelectedItem);
        if (focus && ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container)
        {
            container.Focus();
        }
    }

    private void ScheduleSelectedIntoView()
    {
        if (_revealScheduled)
        {
            return;
        }
        _revealScheduled = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _revealScheduled = false;
            if (SelectedItem is not null)
            {
                ScrollIntoView(SelectedItem);
                // BringIntoView excludes the item's outer margin. At either end,
                // finish at the edge so arrows do not offer scrolling into that gap.
                if (SelectedIndex == 0)
                {
                    _scroller?.ScrollToLeftEnd();
                }
                else if (SelectedIndex == Items.Count - 1)
                {
                    _scroller?.ScrollToRightEnd();
                }
            }
            UpdateOverflow();
        });
    }
}

public sealed class InstanceTabCloseRequestedEventArgs(object item) : EventArgs
{
    public object Item { get; } = item;
}

public sealed class InstanceTabNewButtonMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(value is double width && double.IsFinite(width) && width >= 0 ? width + 5 : 5, 0, 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
