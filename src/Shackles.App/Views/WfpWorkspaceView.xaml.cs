using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Shackles.Wfp;

namespace Shackles.App.Views;

public sealed partial class WfpWorkspaceView : UserControl, IDisposable
{
    private const int ErrorCancelled = 1223;
    private readonly ObservableCollection<WfpRuleRow> _ruleRows = [];
    private readonly ObservableCollection<InterfaceChoice> _interfaceChoices = [];
    private readonly bool _hasRequiredIntegrity;
    private readonly string? _integrityCheckFailure;
    private WfpSupportInfo? _support;
    private WfpSession? _session;
    private bool _isBusy;
    private bool _prepared;
    private bool _disposed;

    public WfpWorkspaceView()
    {
        InitializeComponent();

        try
        {
            _hasRequiredIntegrity = WfpSupport.IsCurrentProcessHighIntegrity();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _hasRequiredIntegrity = false;
            _integrityCheckFailure = exception.Message;
        }

        ActiveRuleList.ItemsSource = _ruleRows;
        _interfaceChoices.Add(new InterfaceChoice("Any interface", null));
        InterfaceComboBox.ItemsSource = _interfaceChoices;
        InterfaceComboBox.SelectedIndex = 0;
        SetCurrentUserScopeText();
        ConfigureIntegrityGate();
        UpdateActionState();
    }

    public bool IsBusy => _isBusy;

    public bool HasActivePolicy => _ruleRows.Count > 0;

    public int ActiveRuleCount => _ruleRows.Count;

    public void PrepareForDisplay()
    {
        if (_disposed || !_hasRequiredIntegrity || _prepared)
        {
            _prepared = true;
            return;
        }

        _prepared = true;
        _ = RefreshSupportAsync();
    }

    private async void RefreshSupport_Click(object sender, RoutedEventArgs e) =>
        await RefreshSupportAsync().ConfigureAwait(true);

    private async Task RefreshSupportAsync()
    {
        if (_disposed || _isBusy || !_hasRequiredIntegrity)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await Task.Run(() =>
            {
                var support = WfpSupport.Probe();
                IReadOnlyList<WfpNetworkInterface> interfaces = [];
                string? interfaceWarning = null;
                if (support.IsAvailable)
                {
                    try
                    {
                        interfaces = WfpNetworkInterfaces.GetAll();
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        interfaceWarning = exception.Message;
                    }
                }

                return (
                    Support: support,
                    Interfaces: interfaces,
                    InterfaceWarning: interfaceWarning);
            }).ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            _support = result.Support;
            SupportStateText.Text = result.Support.IsAvailable
                ? "WFP is available"
                : "WFP Blocking is unavailable";
            SupportDetailText.Text = result.InterfaceWarning is null
                ? result.Support.Summary
                : $"{result.Support.Summary} Interface enumeration failed: {result.InterfaceWarning}";
            ReplaceInterfaces(result.Interfaces);
            ShowNotice(result.Support.IsAvailable
                ? result.InterfaceWarning is null
                    ? "WFP is ready. Rules use a dynamic engine session and take effect immediately when their transaction commits."
                    : $"WFP is ready, but interfaces could not be listed: {result.InterfaceWarning} Rules can still target any interface."
                : result.Support.Summary);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (_disposed)
            {
                return;
            }

            _support = null;
            SupportStateText.Text = "WFP support could not be checked";
            SupportDetailText.Text = exception.Message;
            ShowNotice($"WFP support could not be checked: {exception.Message}");
        }
        finally
        {
            if (!_disposed)
            {
                SetBusy(false);
            }
        }
    }

    private void ReplaceInterfaces(IReadOnlyList<WfpNetworkInterface> interfaces)
    {
        var selectedLuid = (InterfaceComboBox.SelectedItem as InterfaceChoice)?.Interface?.Luid;
        _interfaceChoices.Clear();
        _interfaceChoices.Add(new InterfaceChoice("Any interface", null));
        foreach (var item in interfaces)
        {
            var description = string.Equals(item.Name, item.Description, StringComparison.OrdinalIgnoreCase)
                ? item.DisplayName
                : $"{item.DisplayName} — {item.Description}";
            _interfaceChoices.Add(new InterfaceChoice(description, item));
        }

        InterfaceComboBox.SelectedItem = selectedLuid is null
            ? _interfaceChoices[0]
            : _interfaceChoices.FirstOrDefault(item => item.Interface?.Luid == selectedLuid) ??
              _interfaceChoices[0];
    }

    private void ConfigureIntegrityGate()
    {
        InteractiveWorkspace.Visibility = _hasRequiredIntegrity
            ? Visibility.Visible
            : Visibility.Collapsed;
        ElevationGate.Visibility = _hasRequiredIntegrity
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (_hasRequiredIntegrity)
        {
            return;
        }

        RefreshSupportButton.Visibility = Visibility.Collapsed;
        SupportStateText.Text = _integrityCheckFailure is null
            ? "Administrator access required"
            : "Administrator access could not be verified";
        SupportDetailText.Text = _integrityCheckFailure is null
            ? "Open an elevated Shackles window before configuring WFP Blocking."
            : "The WFP workspace is locked because Shackles could not verify elevation.";
        if (_integrityCheckFailure is not null)
        {
            ElevationLaunchStatusText.Text =
                $"Elevation check failed: {_integrityCheckFailure}";
            ElevationLaunchStatusText.Visibility = Visibility.Visible;
        }
    }

    private void SetCurrentUserScopeText()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            CurrentUserScopeText.Text =
                $"Current user: {identity.Name}. Clear this only when you intend to affect every user who runs the same path.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            CurrentUserScopeText.Text =
                $"Windows could not identify the account for this rule: {exception.Message}";
        }
    }

    private void OpenElevatedWfp_Click(object sender, RoutedEventArgs e)
    {
        if (_hasRequiredIntegrity)
        {
            return;
        }

        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException(
                    "Windows could not determine the Shackles application path.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };
            if (string.Equals(
                    Path.GetFileNameWithoutExtension(executablePath),
                    "dotnet",
                    StringComparison.OrdinalIgnoreCase))
            {
                var applicationAssemblyPath = typeof(App).Assembly.Location;
                if (string.IsNullOrWhiteSpace(applicationAssemblyPath))
                {
                    throw new InvalidOperationException(
                        "Windows could not determine the Shackles application assembly path.");
                }

                startInfo.ArgumentList.Add(applicationAssemblyPath);
            }

            startInfo.ArgumentList.Add(App.WfpWorkspaceArgument);
            using var elevatedProcess = Process.Start(startInfo);
            if (elevatedProcess is null)
            {
                throw new InvalidOperationException(
                    "Windows did not start the administrator copy of Shackles.");
            }

            OpenElevatedWfpButton.IsEnabled = false;
            ElevationLaunchStatusText.Text =
                "An administrator copy is opening directly on WFP Blocking. This window remains open.";
            ElevationLaunchStatusText.Visibility = Visibility.Visible;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            ElevationLaunchStatusText.Text =
                "The administrator prompt was canceled. You can try again when you are ready.";
            ElevationLaunchStatusText.Visibility = Visibility.Visible;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ElevationLaunchStatusText.Text =
                $"Shackles could not open an administrator copy: {exception.Message}";
            ElevationLaunchStatusText.Visibility = Visibility.Visible;
        }
    }

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the executable for WFP Blocking",
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            ExecutablePathTextBox.Text = dialog.FileName;
        }
    }

    private void Protocol_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LocalPortTextBox is null || RemotePortTextBox is null)
        {
            return;
        }

        var portsAvailable = ProtocolComboBox.SelectedIndex is 1 or 2;
        LocalPortTextBox.IsEnabled = portsAvailable;
        RemotePortTextBox.IsEnabled = portsAvailable;
        if (!portsAvailable)
        {
            LocalPortTextBox.Clear();
            RemotePortTextBox.Clear();
        }
    }

    private async void AddRule_Click(object sender, RoutedEventArgs e)
    {
        if (_disposed || _isBusy)
        {
            return;
        }

        WfpBlockRuleOptions options;
        try
        {
            options = WfpSession.ValidateBlockRule(BuildOptions());
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            ShowInputError(exception.Message);
            return;
        }

        if (!options.CurrentUserOnly)
        {
            var answer = MessageBox.Show(
                Window.GetWindow(this),
                "This rule will affect every user that runs this exact executable path, and it takes effect immediately. Add the all-users block?",
                "Confirm all-users WFP rule",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        SetBusy(true);
        WfpSession? createdSession = null;
        try
        {
            var session = _session;
            if (session is null)
            {
                session = await Task.Run(WfpSession.Open).ConfigureAwait(true);
                createdSession = session;
            }

            var installed = await Task.Run(() => session.AddBlockRule(options))
                .ConfigureAwait(true);
            _session = session;
            createdSession = null;
            _ruleRows.Add(WfpRuleRow.From(installed));
            ShowNotice(
                $"Added {installed.Filters.Count} atomic WFP filter{(installed.Filters.Count == 1 ? string.Empty : "s")} for {Path.GetFileName(installed.Options.ExecutablePath)}.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            createdSession?.Dispose();
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                "Could not add WFP block rule",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ShowNotice($"The WFP rule was not added: {exception.Message}");
        }
        finally
        {
            if (!_disposed)
            {
                SetBusy(false);
            }
        }
    }

    private WfpBlockRuleOptions BuildOptions()
    {
        var directions = WfpTrafficDirection.None;
        if (OutboundCheckBox.IsChecked == true)
        {
            directions |= WfpTrafficDirection.Outbound;
        }

        if (InboundCheckBox.IsChecked == true)
        {
            directions |= WfpTrafficDirection.Inbound;
        }

        var ipVersions = WfpIpVersion.None;
        if (Ipv4CheckBox.IsChecked == true)
        {
            ipVersions |= WfpIpVersion.Ipv4;
        }

        if (Ipv6CheckBox.IsChecked == true)
        {
            ipVersions |= WfpIpVersion.Ipv6;
        }

        var protocol = ProtocolComboBox.SelectedIndex switch
        {
            0 => WfpTransportProtocol.Any,
            1 => WfpTransportProtocol.Tcp,
            2 => WfpTransportProtocol.Udp,
            3 => WfpTransportProtocol.Icmp,
            _ => throw new ArgumentException("Choose a supported protocol.")
        };
        var interfaceChoice = InterfaceComboBox.SelectedItem as InterfaceChoice;
        return new WfpBlockRuleOptions(
            ExecutablePathTextBox.Text,
            directions,
            ipVersions,
            protocol,
            ParsePort(LocalPortTextBox.Text, "Local port"),
            ParsePort(RemotePortTextBox.Text, "Remote port"),
            NullIfWhiteSpace(LocalNetworkTextBox.Text),
            NullIfWhiteSpace(RemoteNetworkTextBox.Text),
            interfaceChoice?.Interface?.Luid,
            interfaceChoice?.Interface?.Name,
            CurrentUserOnlyCheckBox.IsChecked == true);
    }

    private async void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (_disposed || _isBusy ||
            sender is not Button { Tag: WfpRuleRow row } ||
            _session is not { } session)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var removed = await Task.Run(() => session.RemoveRule(row.Rule.RuleKey))
                .ConfigureAwait(true);
            if (!removed)
            {
                ShowNotice("That WFP rule was no longer present in this session.");
                return;
            }

            _ruleRows.Remove(row);
            if (_ruleRows.Count == 0)
            {
                var close = await Task.Run(session.Close).ConfigureAwait(true);
                if (close.DynamicSessionClosed || close.ExplicitRemovalSucceeded)
                {
                    session.Dispose();
                    _session = null;
                }

                ShowNotice(close.Warnings.Count == 0
                    ? "The last rule was removed and the empty dynamic WFP session was closed."
                    : string.Join(" ", close.Warnings));
            }
            else
            {
                ShowNotice($"Removed the WFP rule for {row.ExecutableName}.");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                "Could not remove WFP block rule",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ShowNotice($"The WFP rule could not be removed: {exception.Message}");
        }
        finally
        {
            if (!_disposed)
            {
                SetBusy(false);
            }
        }
    }

    private async void CloseSession_Click(object sender, RoutedEventArgs e)
    {
        if (_disposed || _isBusy || _session is null)
        {
            return;
        }

        var answer = MessageBox.Show(
            Window.GetWindow(this),
            $"Remove {_ruleRows.Count} Shackles WFP block rule{(_ruleRows.Count == 1 ? string.Empty : "s")}? Other firewall and WFP policy will remain in force.",
            "Close WFP Blocking session",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await CloseSessionAsync().ConfigureAwait(true);
        }
        finally
        {
            if (!_disposed)
            {
                SetBusy(false);
            }
        }
    }

    private async Task CloseSessionAsync()
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        var result = await Task.Run(session.Close).ConfigureAwait(true);
        if (result.ExplicitRemovalSucceeded)
        {
            _ruleRows.Clear();
        }

        if (result.DynamicSessionClosed)
        {
            session.Dispose();
            _session = null;
            _ruleRows.Clear();
            ShowNotice(result.Warnings.Count == 0
                ? "All Shackles WFP filters were deleted and the dynamic session was closed."
                : string.Join(" ", result.Warnings));
        }
        else
        {
            if (result.ExplicitRemovalSucceeded)
            {
                session.Dispose();
                _session = null;
            }

            var details = result.Warnings.Count == 0
                ? "WFP did not confirm that the session closed."
                : string.Join(Environment.NewLine + Environment.NewLine, result.Warnings);
            MessageBox.Show(
                Window.GetWindow(this),
                details,
                "WFP cleanup needs attention",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ShowNotice(details);
        }
    }

    private void SetBusy(bool value)
    {
        _isBusy = value;
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        if (_disposed)
        {
            return;
        }

        var available = _hasRequiredIntegrity && _support?.IsAvailable == true;
        PolicyEditorPanel.IsEnabled = available && !_isBusy;
        AddRuleButton.IsEnabled = available && !_isBusy;
        RefreshSupportButton.IsEnabled = _hasRequiredIntegrity && !_isBusy;
        CloseSessionButton.IsEnabled = _session is not null && !_isBusy;
        ActiveRuleList.IsEnabled = !_isBusy;
        EmptyRulesText.Visibility = _ruleRows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ActiveRuleList.Visibility = _ruleRows.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        SessionStateText.Text = _session is null
            ? "No active WFP session"
            : $"{_ruleRows.Count} active block rule{(_ruleRows.Count == 1 ? string.Empty : "s")}";
        SessionDetailText.Text = _session is null
            ? "The first rule opens a dynamic engine session."
            : $"Session {_session.SessionKey:N}. Normal close deletes every filter explicitly; BFE removes the dynamic session if Shackles exits unexpectedly.";
    }

    private void ShowNotice(string message) => NoticeText.Text = message;

    private void ShowInputError(string message)
    {
        MessageBox.Show(
            Window.GetWindow(this),
            message,
            "Check the WFP rule",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        ShowNotice(message);
    }

    private static ushort? ParsePort(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!ushort.TryParse(
                text.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var port) || port == 0)
        {
            throw new ArgumentException($"{label} must be a number from 1 through 65535.");
        }

        return port;
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var session = _session;
        _session = null;
        session?.Dispose();
        _ruleRows.Clear();
    }

    private sealed record InterfaceChoice(
        string DisplayName,
        WfpNetworkInterface? Interface);

    private sealed record WfpRuleRow(
        WfpInstalledRule Rule,
        string ExecutableName,
        string ExecutablePath,
        string ScopeSummary,
        string IdentitySummary)
    {
        internal static WfpRuleRow From(WfpInstalledRule rule)
        {
            var options = rule.Options;
            var scope = new List<string>
            {
                DescribeDirections(options.Directions),
                DescribeVersions(options.IpVersions),
                options.Protocol == WfpTransportProtocol.Any
                    ? "any protocol"
                    : options.Protocol.ToString().ToUpperInvariant()
            };
            if (options.LocalPort is { } localPort)
            {
                scope.Add($"local port {localPort}");
            }

            if (options.RemotePort is { } remotePort)
            {
                scope.Add($"remote port {remotePort}");
            }

            if (options.LocalNetwork is not null)
            {
                scope.Add($"local {options.LocalNetwork}");
            }

            if (options.RemoteNetwork is not null)
            {
                scope.Add($"remote {options.RemoteNetwork}");
            }

            if (options.InterfaceLuid is not null)
            {
                scope.Add($"interface {options.InterfaceName ?? "selected LUID"}");
            }

            return new WfpRuleRow(
                rule,
                Path.GetFileName(options.ExecutablePath),
                options.ExecutablePath,
                string.Join(" · ", scope),
                $"{(options.CurrentUserOnly ? "Current user" : "All users")} · " +
                $"{rule.Filters.Count} filter{(rule.Filters.Count == 1 ? string.Empty : "s")} · " +
                $"rule {rule.RuleKey:N}");
        }

        private static string DescribeDirections(WfpTrafficDirection value) => value switch
        {
            WfpTrafficDirection.Both => "inbound + outbound",
            WfpTrafficDirection.Inbound => "inbound",
            WfpTrafficDirection.Outbound => "outbound",
            _ => value.ToString()
        };

        private static string DescribeVersions(WfpIpVersion value) => value switch
        {
            WfpIpVersion.All => "IPv4 + IPv6",
            WfpIpVersion.Ipv4 => "IPv4",
            WfpIpVersion.Ipv6 => "IPv6",
            _ => value.ToString()
        };
    }
}
