using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shackles.App.Views;

namespace Shackles.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WorkspaceStyleConsistencyTests
{
    [TestMethod]
    [DataRow("ActionButtonStyle")]
    [DataRow("PrimaryButtonStyle")]
    [DataRow("CompactButtonStyle")]
    [DataRow("RowActionButtonStyle")]
    public async Task ButtonVariantsShareBlueChromeAndDisabledStates(string styleName)
    {
        await OnUi(() =>
        {
            var button = new Button { Style = Resource<Style>(styleName), Content = "_Action" };
            var standard = new Button { Style = Resource<Style>("ActionButtonStyle") };
            button.Measure(new Size(300, 100));
            button.Arrange(new Rect(button.DesiredSize));
            standard.ApplyTemplate();

            Assert.AreSame(standard.Template, button.Template);
            Assert.AreEqual(32d, button.MinHeight);
            var chrome = (Border)button.Template.FindName("Chrome", button);
            Assert.AreEqual(Resource<SolidColorBrush>("AccentBrush").Color, ((SolidColorBrush)chrome.Background).Color);
            Assert.AreEqual(Resource<SolidColorBrush>("AccentTextBrush").Color, ((SolidColorBrush)button.Foreground).Color);
            Assert.AreEqual(new CornerRadius(4), chrome.CornerRadius);
            Assert.IsTrue(button.Template.Triggers.OfType<Trigger>().Any(trigger => trigger.Property == UIElement.IsKeyboardFocusedProperty));
            Assert.IsTrue(button.Template.Triggers.OfType<Trigger>().Any(trigger => trigger.Property == ButtonBase.IsPressedProperty));
            var overlay = (Border)button.Template.FindName("StateOverlay", button);
            Assert.IsNull(overlay.Child);
            foreach (var state in button.Template.Triggers.OfType<Trigger>()
                         .Where(trigger => trigger.Property == UIElement.IsMouseOverProperty || trigger.Property == ButtonBase.IsPressedProperty))
            {
                var setter = state.Setters.OfType<Setter>().Single();
                Assert.AreEqual("StateOverlay", setter.TargetName, "Interaction feedback must keep button labels opaque.");
            }

            button.IsEnabled = false;
            button.UpdateLayout();
            Assert.AreEqual(Resource<SolidColorBrush>("MutedPanelBrush").Color, ((SolidColorBrush)chrome.Background).Color);
            Assert.AreEqual(Resource<SolidColorBrush>("MutedTextBrush").Color, ((SolidColorBrush)button.Foreground).Color);
            Assert.AreEqual(0d, overlay.Opacity);
        });
    }

    [TestMethod]
    public async Task WorkspaceActionsUseExplicitSharedStyles()
    {
        await OnUi(() =>
        {
            using var window = new MainWindow();
            var shared = new HashSet<Style>
            {
                Resource<Style>("ActionButtonStyle"), Resource<Style>("PrimaryButtonStyle"),
                Resource<Style>("CompactButtonStyle"), Resource<Style>("RowActionButtonStyle")
            };
            var buttons = LogicalDescendants(window).OfType<Button>()
                .Concat(LogicalDescendants(new JobDetailsView()).OfType<Button>()).ToArray();
            Assert.IsTrue(buttons.Length > 30);
            foreach (var button in buttons)
            {
                Assert.IsTrue(shared.Contains(button.Style), $"Button '{button.Content}' uses a different style.");
            }
            window.Dispose();
            window.Close();
        });
    }

    [TestMethod]
    public async Task SandboxEmptyPagesAndEditorColumnsMatch()
    {
        await OnUi(() =>
        {
            using var app = new AppContainerWorkspaceView(() => throw new InvalidOperationException("No initialization needed."));
            using var experimental = new ExperimentalSandboxWorkspaceView(() => throw new InvalidOperationException("No initialization needed."));
            var appEmpty = (Border)app.FindName("EmptyEditorState");
            var experimentalEmpty = (Border)experimental.FindName("EmptyEditorState");
            Assert.AreSame(appEmpty.Style, experimentalEmpty.Style);
            Assert.AreEqual(((SolidColorBrush)appEmpty.Background).Color, ((SolidColorBrush)experimentalEmpty.Background).Color);
            var emptyNotice = (TextBlock)app.FindName("EmptyWorkspaceNoticeText");
            Assert.AreEqual(Resource<SolidColorBrush>("TextBrush").Color, ((SolidColorBrush)emptyNotice.Foreground).Color);

            var appScroll = (ScrollViewer)app.FindName("SandboxEditorScrollViewer");
            var experimentalScroll = (ScrollViewer)experimental.FindName("SandboxEditorScrollViewer");
            Assert.AreEqual(ScrollBarVisibility.Disabled, appScroll.HorizontalScrollBarVisibility);
            Assert.AreEqual(appScroll.HorizontalScrollBarVisibility, experimentalScroll.HorizontalScrollBarVisibility);
            Assert.AreEqual(((StackPanel)appScroll.Content).Margin, ((StackPanel)experimentalScroll.Content).Margin);
            var appColumns = ((Grid)((Border)appScroll.Parent).Parent).ColumnDefinitions;
            var experimentalColumns = ((Grid)((Border)experimentalScroll.Parent).Parent).ColumnDefinitions;
            Assert.HasCount(3, appColumns);
            Assert.HasCount(3, experimentalColumns);
            for (var index = 0; index < appColumns.Count; index++)
            {
                Assert.AreEqual(appColumns[index].Width, experimentalColumns[index].Width);
                Assert.AreEqual(appColumns[index].MinWidth, experimentalColumns[index].MinWidth);
            }
        });
    }

    [TestMethod]
    public async Task SharedTextAndNoticeStylesKeepTheirPurpose()
    {
        await OnUi(() =>
        {
            var heading = new TextBlock { Style = Resource<Style>("StepTitleStyle") };
            var value = new TextBlock { Style = Resource<Style>("SummaryValueStyle") };
            Assert.AreEqual(16d, heading.FontSize);
            Assert.AreEqual(FontWeights.SemiBold, heading.FontWeight);
            Assert.AreEqual(TextWrapping.Wrap, value.TextWrapping);

            using var wesp = new WespWorkspaceView();
            using var wfp = new WfpWorkspaceView();
            Assert.AreSame(Resource<Style>("NoticeBorderStyle"), ((Border)wesp.FindName("WorkspaceNotice")).Style);
            var notice = (TextBox)wfp.FindName("NoticeText");
            Assert.IsTrue(notice.IsReadOnly);
            Assert.AreEqual(120d, notice.MaxHeight);
            Assert.AreEqual(ScrollBarVisibility.Auto, notice.VerticalScrollBarVisibility);
            Assert.AreEqual(Resource<SolidColorBrush>("TextBrush").Color, ((SolidColorBrush)notice.Foreground).Color);
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task BlockingWorkspacesShareCompactHeadersAndAdministratorChrome(bool isWesp)
    {
        await OnUi(() =>
        {
            using var fixture = new BlockingWorkspaceFixture(isWesp);
            var view = fixture.View;
            var header = StyledElement<Border>(view, "CompactWorkspaceHeaderStyle");
            var support = StyledElement<Border>(view, "SupportStatusPanelStyle");
            var card = StyledElement<Border>(view, "AdministratorPromptCardStyle");
            var icon = StyledElement<Border>(view, "AdministratorPromptIconStyle");
            var title = StyledElement<TextBlock>(view, "AdministratorPromptTitleStyle");
            var body = StyledElement<TextBlock>(view, "AdministratorPromptBodyStyle");
            var supportDetails = StyledElement<ScrollViewer>(view, "SupportDetailScrollViewerStyle");

            Assert.AreSame(Resource<Style>("WorkspaceHeaderStyle"), header.Style.BasedOn);
            Assert.AreEqual(new Thickness(14, 9, 14, 9), header.Padding);
            Assert.AreEqual(new Thickness(9, 5, 9, 5), support.Padding);
            Assert.AreEqual(new CornerRadius(4), support.CornerRadius);
            Assert.AreSame(Resource<Style>("CardBorderStyle"), card.Style.BasedOn);
            Assert.AreEqual(620d, card.MaxWidth);
            Assert.AreEqual(480d, card.MinWidth);
            Assert.AreEqual(new Thickness(32), card.Margin);
            Assert.AreEqual(new Thickness(0), card.Padding);
            Assert.AreEqual(HorizontalAlignment.Center, card.HorizontalAlignment);
            Assert.AreEqual(VerticalAlignment.Center, card.VerticalAlignment);
            Assert.AreEqual(58d, icon.Width);
            Assert.AreEqual(58d, icon.Height);
            Assert.AreEqual(new CornerRadius(29), icon.CornerRadius);
            Assert.AreEqual(Resource<SolidColorBrush>("AccentBrush").Color, ((SolidColorBrush)icon.Background).Color);
            Assert.AreSame(Resource<Style>("SectionTitleStyle"), title.Style.BasedOn);
            Assert.AreEqual(22d, title.FontSize);
            Assert.AreEqual(FontWeights.SemiBold, title.FontWeight);
            Assert.AreEqual(14d, body.FontSize);
            Assert.AreEqual(TextWrapping.Wrap, body.TextWrapping);
            Assert.AreEqual(48d, supportDetails.MaxHeight);
            Assert.AreEqual(ScrollBarVisibility.Auto, supportDetails.VerticalScrollBarVisibility);
            Assert.AreEqual(ScrollBarVisibility.Disabled, supportDetails.HorizontalScrollBarVisibility);

            AssertNoLocalValues(header, Border.PaddingProperty);
            AssertNoLocalValues(support, Border.PaddingProperty, Border.BackgroundProperty,
                Border.BorderBrushProperty, Border.BorderThicknessProperty, Border.CornerRadiusProperty);
            AssertNoLocalValues(card, FrameworkElement.MaxWidthProperty, FrameworkElement.MinWidthProperty,
                FrameworkElement.MarginProperty, Border.PaddingProperty,
                FrameworkElement.HorizontalAlignmentProperty, FrameworkElement.VerticalAlignmentProperty);
            AssertNoLocalValues(icon, FrameworkElement.WidthProperty, FrameworkElement.HeightProperty,
                Border.BackgroundProperty, Border.CornerRadiusProperty);
            AssertNoLocalValues(title, TextBlock.FontSizeProperty, TextBlock.FontWeightProperty);
            AssertNoLocalValues(body, TextBlock.FontSizeProperty);
            AssertNoLocalValues(supportDetails, FrameworkElement.MaxHeightProperty,
                ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollViewer.HorizontalScrollBarVisibilityProperty);

            var shield = LogicalDescendants(icon).OfType<System.Windows.Shapes.Path>().Single();
            Assert.AreSame(Resource<Geometry>("AdministratorShieldGeometry"), shield.Data);
            var status = (TextBlock)view.FindName("ElevationLaunchStatusText");
            Assert.AreEqual(Resource<SolidColorBrush>("TextBrush").Color, ((SolidColorBrush)status.Foreground).Color);
            var backdrop = ((Grid)view.FindName("ElevationGate")).Children.OfType<Border>().First();
            Assert.AreSame(Resource<Style>("OptionCardStyle"), backdrop.Style);
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task BlockingFormsUseSpacedLabelsAndSharedRuleSurfaces(bool isWesp)
    {
        await OnUi(() =>
        {
            using var fixture = new BlockingWorkspaceFixture(isWesp);
            var view = fixture.View;
            var style = Resource<Style>("StackedFieldLabelStyle");
            Assert.AreSame(Resource<Style>("FieldLabelStyle"), style.BasedOn);
            var labels = LogicalDescendants(view).OfType<TextBlock>().Where(label => label.Style == style).ToArray();
            Assert.IsTrue(labels.Length >= (isWesp ? 3 : 6), "The blocking form's stacked labels must use the shared spacing style.");
            foreach (var label in labels)
            {
                Assert.AreEqual(3d, label.Margin.Bottom, $"Label '{label.Text}' must have space before its field.");
            }

            var interactive = (Grid)view.FindName("InteractiveWorkspace");
            var scrollers = LogicalDescendants(interactive).OfType<ScrollViewer>().ToArray();
            Assert.IsTrue(scrollers.Length >= (isWesp ? 2 : 1));
            foreach (var scroller in scrollers)
            {
                Assert.AreEqual(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
                Assert.AreEqual(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
            }

            if (!isWesp)
            {
                var close = (Button)view.FindName("CloseSessionButton");
                Assert.AreSame(Resource<Style>("CompactButtonStyle"), close.Style);
                var rules = (ListBox)view.FindName("ActiveRuleList");
                var row = (Border)rules.ItemTemplate.LoadContent();
                Assert.AreSame(Resource<Style>("OptionCardStyle"), row.Style);
                AssertNoLocalValues(row, Border.PaddingProperty, Border.BackgroundProperty,
                    Border.BorderBrushProperty, Border.BorderThicknessProperty, Border.CornerRadiusProperty);
            }
        });
    }

    [TestMethod]
    [DataRow(true, false, false)]
    [DataRow(false, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, true, false)]
    [DataRow(true, false, true)]
    [DataRow(false, false, true)]
    [DataRow(true, true, true)]
    [DataRow(false, true, true)]
    public async Task BlockingFormsAndAdministratorPromptsRemainUsableAtMinimumWindowSize(
        bool isWesp, bool showAdministratorPrompt, bool longSupportDetails)
    {
        await OnUi(() =>
        {
            using var window = new MainWindow();
            try
            {
                var selectedName = isWesp ? "WespWorkspace" : "WfpWorkspace";
                ReadOnlySpan<string> workspaceNames = ["JobObjectsWorkspace", "AppContainerWorkspace",
                    "ExperimentalSandboxWorkspace", "WespWorkspace", "WfpWorkspace"];
                foreach (var name in workspaceNames)
                {
                    ((FrameworkElement)window.FindName(name)).Visibility = name == selectedName
                        ? Visibility.Visible : Visibility.Collapsed;
                }

                var view = (UserControl)window.FindName(selectedName);
                view.FontSize = 13;
                view.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
                ((Grid)view.FindName("InteractiveWorkspace")).Visibility = showAdministratorPrompt
                    ? Visibility.Collapsed : Visibility.Visible;
                ((Grid)view.FindName("ElevationGate")).Visibility = showAdministratorPrompt
                    ? Visibility.Visible : Visibility.Collapsed;
                var refreshSupport = (Button)view.FindName("RefreshSupportButton");
                refreshSupport.Visibility = showAdministratorPrompt ? Visibility.Collapsed : Visibility.Visible;
                refreshSupport.IsEnabled = !showAdministratorPrompt;
                ((TextBlock)view.FindName("SupportStateText")).Text = showAdministratorPrompt
                    ? "Administrator access required" : isWesp ? "WESP available" : "BFE available";
                ((TextBlock)view.FindName("SupportDetailText")).Text = showAdministratorPrompt
                    ? "Open an elevated Shackles window before configuring blocking."
                    : isWesp ? "The WESP client is available on this computer."
                        : "The Base Filtering Engine is running.";
                if (showAdministratorPrompt)
                {
                    var status = (TextBlock)view.FindName("ElevationLaunchStatusText");
                    status.Text = "The administrator prompt was canceled.\nYou can try again when you are ready.";
                    status.Visibility = Visibility.Visible;
                }
                if (longSupportDetails)
                {
                    ((TextBlock)view.FindName("SupportDetailText")).Text = string.Join(Environment.NewLine,
                        Enumerable.Range(1, 40).Select(index => $"Support diagnostic {index}: a detailed Windows status remains available by scrolling."));
                }

                // A 1040x700 native window leaves this client area after its frame.
                var client = (FrameworkElement)window.Content;
                var size = new Size(1024, 661);
                client.Measure(size);
                client.Arrange(new Rect(size));
                client.UpdateLayout();
                Assert.IsTrue(view.ActualWidth >= 952);
                Assert.IsTrue(view.ActualHeight > 450, $"The minimum window left only {view.ActualHeight}px for this workspace.");
                if (refreshSupport.Visibility == Visibility.Visible)
                {
                    AssertFits(refreshSupport, view);
                }
                var supportDetails = StyledElement<ScrollViewer>(view, "SupportDetailScrollViewerStyle");
                Assert.IsTrue(supportDetails.ActualHeight <= 48, $"Support details used {supportDetails.ActualHeight}px.");
                if (longSupportDetails)
                {
                    Assert.IsTrue(supportDetails.ScrollableHeight > 0, "Long support diagnostics must remain reachable by scrolling.");
                }

                if (showAdministratorPrompt)
                {
                    var card = StyledElement<Border>(view, "AdministratorPromptCardStyle");
                    AssertFits(card, view);
                    AssertFits(StyledElement<TextBlock>(view, "AdministratorPromptTitleStyle"), view);
                    AssertFits(StyledElement<TextBlock>(view, "AdministratorPromptBodyStyle"), view);
                    AssertFits((Button)view.FindName(isWesp ? "OpenElevatedWespButton" : "OpenElevatedWfpButton"), view);
                    AssertFits((TextBlock)view.FindName("ElevationLaunchStatusText"), card);
                }
                else
                {
                    var interactive = (Grid)view.FindName("InteractiveWorkspace");
                    AssertFits(interactive, view);
                    var scrollers = LogicalDescendants(interactive).OfType<ScrollViewer>().ToArray();
                    Assert.IsTrue(scrollers[0].ViewportHeight > 100, $"Editor viewport was {scrollers[0].ViewportHeight}px.");
                    Assert.IsTrue(scrollers[0].ScrollableHeight > 0, "The full policy form should remain reachable by vertical scrolling.");
                    AssertFits((Button)view.FindName(isWesp ? "PrimaryActionButton" : "AddRuleButton"),
                        isWesp ? view : (FrameworkElement)scrollers[0].Content);
                    if (isWesp)
                    {
                        var editor = (Border)scrollers[0].Parent;
                        var activity = interactive.Children.OfType<Border>().Single(border => Grid.GetRow(border) == 2);
                        Assert.IsTrue(editor.ActualHeight >= 200, $"Editor panel was {editor.ActualHeight}px.");
                        Assert.IsTrue(activity.ActualHeight >= 100, $"Activity panel was {activity.ActualHeight}px.");
                        AssertFits(activity, view);
                        var emptyActivity = (TextBlock)view.FindName("EmptySessionActivityText");
                        Assert.IsTrue(emptyActivity.ActualHeight >= emptyActivity.FontSize * 1.2,
                            "The empty activity hint needs enough height for a complete line of text.");
                        AssertFits(emptyActivity, (Grid)emptyActivity.Parent);
                        AssertFits(emptyActivity, view);
                        Assert.IsTrue(scrollers[1].ViewportHeight > 40, $"Session summary viewport was {scrollers[1].ViewportHeight}px.");
                        AssertFits((Button)view.FindName("ResetDraftButton"), view);
                    }
                }
            }
            finally
            {
                window.Dispose();
                window.Close();
            }
        });
    }

    private static T StyledElement<T>(DependencyObject root, string styleName) where T : FrameworkElement =>
        LogicalDescendants(root).OfType<T>().Single(element => element.Style == Resource<Style>(styleName));

    private static void AssertNoLocalValues(DependencyObject element, params DependencyProperty[] properties)
    {
        foreach (var property in properties)
        {
            Assert.AreSame(DependencyProperty.UnsetValue, element.ReadLocalValue(property),
                $"{element.GetType().Name}.{property.Name} must come from the shared style.");
        }
    }

    private static void AssertFits(FrameworkElement element, FrameworkElement ancestor)
    {
        var bounds = element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
        Assert.IsTrue(bounds.Width > 0 && bounds.Height > 0, $"{element.Name} was not laid out.");
        Assert.IsTrue(bounds.Left >= -0.5 && bounds.Right <= ancestor.ActualWidth + 0.5,
            $"{element.Name} horizontal bounds {bounds} exceed {ancestor.ActualWidth}px.");
        Assert.IsTrue(bounds.Top >= -0.5 && bounds.Bottom <= ancestor.ActualHeight + 0.5,
            $"{element.Name} vertical bounds {bounds} exceed {ancestor.ActualHeight}px.");
    }

    private sealed class BlockingWorkspaceFixture(bool isWesp) : IDisposable
    {
        public UserControl View { get; } = isWesp ? new WespWorkspaceView() : new WfpWorkspaceView();
        public void Dispose() => ((IDisposable)View).Dispose();
    }

    private static T Resource<T>(string name) => (T)Application.Current.FindResource(name);

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalDescendants(child)) { yield return descendant; }
        }
    }

    private static Task OnUi(Action action) => SandboxWorkspaceLifecycleTests.OnUi(() =>
    {
        action();
        return Task.CompletedTask;
    });
}
