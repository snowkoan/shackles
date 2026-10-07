using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using Shackles.App.Controls;

namespace Shackles.App.Tests;

[TestClass]
public sealed class InstanceTabStripTests
{
    [TestMethod]
    public async Task CloseButtonTargetsBackgroundItemWithoutChangingSelection()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var first = new TestInstance("First");
            var background = new TestInstance("Background");
            var strip = CreateStrip(first, background);
            strip.SelectedItem = first;
            await Layout(strip);
            object? requested = null;
            strip.CloseRequested += (_, args) => requested = args.Item;
            var container = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromItem(background);
            var button = (Button)container.Template.FindName("PART_CloseButton", container);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.AreSame(background, requested);
            Assert.AreSame(first, strip.SelectedItem);
            Assert.AreEqual("Close Background", AutomationProperties.GetName(button));
            Assert.IsFalse(button.Focusable, "Pointer close must not move selection through keyboard focus.");
        });
    }

    [TestMethod]
    public async Task PerItemCloseGatingUpdatesWithoutBlockingSelection()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var first = new TestInstance("First");
            var busy = new TestInstance("Busy") { CanClose = false };
            var strip = CreateStrip(first, busy);
            strip.CloseEnabledMemberPath = nameof(TestInstance.CanClose);
            await Layout(strip);
            var container = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromItem(busy);
            var button = (Button)container.Template.FindName("PART_CloseButton", container);
            var requests = 0;
            strip.CloseRequested += (_, _) => requests++;

            Assert.IsFalse(button.IsEnabled);
            Assert.IsFalse(strip.RequestClose(busy));
            strip.SelectedItem = busy;
            Assert.AreSame(busy, strip.SelectedItem);
            busy.CanClose = true;
            await Dispatcher.Yield(DispatcherPriority.DataBind);

            Assert.IsTrue(button.IsEnabled);
            Assert.IsTrue(strip.RequestClose(busy));
            Assert.AreEqual(1, requests);
        });
    }

    [TestMethod]
    public async Task PlusRaisesRequestOnlyWhenEnabledAndKeepsSelection()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var item = new TestInstance("Selected");
            var strip = CreateStrip(item);
            strip.SelectedItem = item;
            strip.IsNewEnabled = false;
            strip.NewToolTip = "Create another sandbox";
            await Layout(strip);
            var button = (Button)strip.Template.FindName("PART_NewButton", strip);
            var requests = 0;
            strip.NewRequested += (_, args) => { requests++; Assert.AreSame(strip, args.Source); };

            Assert.IsFalse(button.IsEnabled);
            Assert.AreEqual("Create another sandbox", button.ToolTip);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(0, requests);
            strip.IsNewEnabled = true;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.AreEqual(1, requests);
            Assert.AreSame(item, strip.SelectedItem);
        });
    }

    [TestMethod]
    public async Task KeyboardTabStopsIncludeSelectedInstanceAndPlus()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var first = new TestInstance("First");
            var selected = new TestInstance("Selected");
            var strip = CreateStrip(first, selected);
            strip.SelectedItem = selected;
            await Layout(strip);
            var firstContainer = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromItem(first);
            var selectedContainer = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromItem(selected);
            var plus = (Button)strip.Template.FindName("PART_NewButton", strip);

            Assert.AreEqual(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(strip));
            Assert.IsFalse(firstContainer.IsTabStop);
            Assert.IsTrue(selectedContainer.IsTabStop);
            Assert.IsTrue(plus.IsTabStop);
            Assert.IsTrue(plus.Focusable);

            strip.SelectedItem = first;
            Assert.IsTrue(firstContainer.IsTabStop);
            Assert.IsFalse(selectedContainer.IsTabStop);
        });
    }

    [TestMethod]
    public async Task PlusFollowsFittingTabsAndStaysVisibleWithOverflow()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var items = new[] { new TestInstance("One"), new TestInstance("Two"), new TestInstance("Three") };
            var strip = CreateStrip(items);
            await Layout(strip, width: 700);
            var last = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromItem(items[^1]);
            var plus = (Button)strip.Template.FindName("PART_NewButton", strip);
            var lastBounds = last.TransformToAncestor(strip).TransformBounds(new Rect(last.RenderSize));
            var plusBounds = plus.TransformToAncestor(strip).TransformBounds(new Rect(plus.RenderSize));

            Assert.IsFalse(strip.HasOverflow);
            Assert.IsTrue(plusBounds.Left >= lastBounds.Right, $"Plus starts {plusBounds.Left}; last tab ends {lastBounds.Right}.");
            Assert.IsTrue(plusBounds.Left - lastBounds.Right <= 16, $"Gap after last tab is {plusBounds.Left - lastBounds.Right}.");
            Assert.IsTrue(plusBounds.Right <= strip.ActualWidth + 1);

            await Layout(strip, width: 400);
            plusBounds = plus.TransformToAncestor(strip).TransformBounds(new Rect(plus.RenderSize));
            Assert.IsTrue(strip.HasOverflow);
            Assert.IsTrue(plusBounds.Left >= 0);
            Assert.IsTrue(plusBounds.Right <= strip.ActualWidth + 1);
            Assert.AreEqual(strip.ActualWidth, plusBounds.Right, 1);
        });
    }

    [TestMethod]
    public async Task KeyboardCyclesAndWrapsWhileEditorArrowsRemainUntouched()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var strip = CreateStrip(new("One"), new("Two"), new("Three"));
            strip.SelectedIndex = 2;
            await Layout(strip);

            Assert.IsFalse(strip.HandleNavigationKey(Key.Left, ModifierKeys.None, fromWorkspace: true));
            Assert.AreEqual(2, strip.SelectedIndex);
            Assert.IsTrue(strip.HandleNavigationKey(Key.Tab, ModifierKeys.Control, fromWorkspace: true));
            Assert.AreEqual(0, strip.SelectedIndex);
            Assert.IsTrue(strip.HandleNavigationKey(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, fromWorkspace: true));
            Assert.AreEqual(2, strip.SelectedIndex);
            Assert.IsTrue(strip.HandleNavigationKey(Key.Home, ModifierKeys.None, fromWorkspace: false));
            Assert.AreEqual(0, strip.SelectedIndex);
            Assert.IsTrue(strip.HandleNavigationKey(Key.Right, ModifierKeys.None, fromWorkspace: false));
            Assert.AreEqual(1, strip.SelectedIndex);
            Assert.IsTrue(strip.HandleNavigationKey(Key.End, ModifierKeys.None, fromWorkspace: false));
            Assert.AreEqual(2, strip.SelectedIndex);
            Assert.IsTrue(strip.HandleNavigationKey(Key.Left, ModifierKeys.None, fromWorkspace: false));
            Assert.AreEqual(1, strip.SelectedIndex);
            Assert.IsFalse(strip.HandleNavigationKey(Key.Tab, ModifierKeys.None, fromWorkspace: false));
        });
    }

    [TestMethod]
    public async Task DisabledStripIgnoresNavigationAndCloseRequests()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var item = new TestInstance("Selected");
            var strip = CreateStrip(item);
            strip.SelectedItem = item;
            await Layout(strip);
            strip.IsEnabled = false;

            Assert.IsFalse(strip.RequestClose(item));
            Assert.IsFalse(strip.HandleNavigationKey(Key.Tab, ModifierKeys.Control, fromWorkspace: true));
            Assert.AreSame(item, strip.SelectedItem);
        });
    }

    [TestMethod]
    public async Task OverflowStaysOnOneRowAndKeepsSelectedTabVisible()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var items = Enumerable.Range(1, 8).Select(index => new TestInstance($"Sandbox instance number {index}")).ToArray();
            var strip = CreateStrip(items);
            await Layout(strip, width: 400);
            Assert.IsTrue(strip.HasOverflow);
            Assert.IsTrue(strip.CanScrollRight);

            strip.SelectedItem = items[^1];
            await Layout(strip, width: 400);
            var scroller = (ScrollViewer)strip.Template.FindName("PART_TabScroller", strip);
            var container = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromItem(items[^1]);
            var bounds = container.TransformToAncestor(scroller).TransformBounds(new Rect(container.RenderSize));

            Assert.IsTrue(scroller.HorizontalOffset > 0);
            Assert.IsTrue(bounds.Left >= -1, $"Selected tab starts at {bounds.Left}.");
            Assert.IsTrue(bounds.Right <= scroller.ViewportWidth + 1, $"Selected tab ends at {bounds.Right}; viewport {scroller.ViewportWidth}.");
            Assert.IsTrue(strip.CanScrollLeft);
            Assert.IsFalse(strip.CanScrollRight,
                $"offset={scroller.HorizontalOffset}; extent={scroller.ExtentWidth}; viewport={scroller.ViewportWidth}");
            foreach (var item in items)
            {
                var other = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromItem(item);
                var otherBounds = other.TransformToAncestor(scroller).TransformBounds(new Rect(other.RenderSize));
                Assert.AreEqual(bounds.Top, otherBounds.Top, 0.01);
            }

            strip.SelectedItem = items[0];
            await Layout(strip, width: 400);
            Assert.AreEqual(0d, scroller.HorizontalOffset, 0.01);
            Assert.IsFalse(strip.CanScrollLeft);
            Assert.IsTrue(strip.CanScrollRight);
        });
    }

    [TestMethod]
    public async Task OverflowArrowsDisappearWhenTheRowFitsWithoutThem()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var strip = CreateStrip(new("One"), new("Two"), new("Three"));
            await Layout(strip, width: 400);
            Assert.IsTrue(strip.HasOverflow);

            await Layout(strip, width: 480);

            Assert.IsFalse(strip.HasOverflow);
            Assert.IsFalse(strip.CanScrollLeft);
            Assert.IsFalse(strip.CanScrollRight);
        });
    }

    [TestMethod]
    public async Task GrowingSelectedHeaderRevealsItButManualScrollingStaysPut()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var items = Enumerable.Range(1, 8).Select(index => new TestInstance($"S{index}")).ToArray();
            var strip = CreateStrip(items);
            strip.SelectedItem = items[2];
            await Layout(strip, width: 400);
            var container = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromItem(items[2]);
            var previousWidth = container.ActualWidth;
            items[2].DisplayName = "A much longer sandbox label that reaches the maximum tab width";

            await Layout(strip, width: 400);
            var scroller = (ScrollViewer)strip.Template.FindName("PART_TabScroller", strip);
            var bounds = container.TransformToAncestor(scroller).TransformBounds(new Rect(container.RenderSize));
            Assert.IsTrue(container.ActualWidth > previousWidth);
            Assert.IsTrue(bounds.Left >= -1, $"Selected tab starts at {bounds.Left}.");
            Assert.IsTrue(bounds.Right <= scroller.ViewportWidth + 1, $"Selected tab ends at {bounds.Right}; viewport {scroller.ViewportWidth}.");
            Assert.AreEqual(items[2].DisplayName, AutomationProperties.GetName(container));

            var requestedOffset = Math.Min(scroller.ScrollableWidth, scroller.HorizontalOffset + 100);
            scroller.ScrollToHorizontalOffset(requestedOffset);
            await Layout(strip, width: 400);
            Assert.AreEqual(requestedOffset, scroller.HorizontalOffset, 0.01, "Offset-only scrolling must not pull the selected tab back.");
        });
    }

    [TestMethod]
    public async Task ReplacingContainerBindingsKeepsAccessibleNameAndCloseStateCurrent()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var item = new TestInstance("Original");
            var strip = CreateStrip(item);
            await Layout(strip);
            var container = (ListBoxItem)strip.ItemContainerGenerator.ContainerFromItem(item);
            Assert.AreEqual("Original", AutomationProperties.GetName(container));

            strip.CloseEnabledMemberPath = nameof(TestInstance.CanClose);
            item.CanClose = false;
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.IsFalse(InstanceTabStrip.GetIsCloseEnabled(container));
            strip.CloseEnabledMemberPath = string.Empty;
            Assert.IsTrue(InstanceTabStrip.GetIsCloseEnabled(container));
            Assert.IsFalse(strip.RequestClose(new TestInstance("Original")), "A matching label is not the same owned item.");
        });
    }

    private static InstanceTabStrip CreateStrip(params TestInstance[] items)
    {
        var strip = new InstanceTabStrip { ItemsSource = items };
        strip.ItemTemplate = (DataTemplate)XamlReader.Parse(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "xmlns:controls='clr-namespace:Shackles.App.Controls;assembly=Shackles'>" +
            "<controls:InstanceTabHeader Text='{Binding DisplayName}' Status='DRAFT' Detail='0 processes' />" +
            "</DataTemplate>");
        return strip;
    }

    private static async Task Layout(InstanceTabStrip strip, double width = 700)
    {
        var size = new Size(width, 52);
        strip.Measure(size);
        strip.Arrange(new Rect(size));
        strip.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        strip.Measure(size);
        strip.Arrange(new Rect(size));
        strip.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        strip.UpdateLayout();
    }

    private sealed class TestInstance(string displayName) : INotifyPropertyChanged
    {
        private string _displayName = displayName;
        private bool _canClose = true;
        public string DisplayName
        {
            get => _displayName;
            set { _displayName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName))); }
        }
        public bool CanClose
        {
            get => _canClose;
            set { _canClose = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanClose))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
