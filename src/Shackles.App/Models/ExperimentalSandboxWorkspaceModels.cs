using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Shackles.ExperimentalSandboxes;

namespace Shackles.App.Models;

internal sealed class ExperimentalSandboxCard : INotifyPropertyChanged
{
    private ExperimentalSandboxSnapshot? _snapshot;

    internal ExperimentalSandboxCard(string initialName)
    {
        Draft = new ExperimentalSandboxDraft();
        Draft.Reset(initialName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal ExperimentalSandboxDraft Draft { get; }

    internal ExperimentalSandbox? Sandbox { get; private set; }

    internal bool IsActive => Sandbox is not null;

    internal ExperimentalSandboxSnapshot? Snapshot => _snapshot;

    public string DisplayName =>
        _snapshot?.DisplayName ??
        (string.IsNullOrWhiteSpace(Draft.Name)
            ? "Untitled sandbox"
            : Draft.Name.Trim());

    public string StateBadge => IsActive ? "ACTIVE" : "DRAFT";

    public string MemberCountText
    {
        get
        {
            var count = _snapshot?.ProcessIds.Count ?? 0;
            return IsActive
                ? $"{count} tracked launch{(count == 1 ? string.Empty : "es")}"
                : "Not created";
        }
    }

    public string PolicySummary
    {
        get
        {
            var options = _snapshot?.Options;
            var appContainer = options?.UseAppContainer ?? Draft.UseAppContainer;
            var rules = options?.FileSystemRules.Count ?? Draft.FileRules.Count;
            var network = options?.NetworkMode ??
                (ExperimentalSandboxNetworkMode)Math.Clamp(
                    Draft.NetworkModeIndex,
                    0,
                    2);
            return $"{(appContainer ? "AppContainer" : "restricted token")} • " +
                   $"{FormatNetwork(network)} • " +
                   $"{rules} path rule{(rules == 1 ? string.Empty : "s")}";
        }
    }

    public string IdentitySummary =>
        _snapshot?.AppContainerSid ??
        (Draft.UseAppContainer
            ? "SID allocated with the first launch"
            : "No AppContainer SID");

    internal void Attach(ExperimentalSandbox sandbox)
    {
        Sandbox = sandbox;
        Refresh();
    }

    internal void Refresh()
    {
        _snapshot = Sandbox?.GetSnapshot();
        NotifyAll();
    }

    internal void RefreshDraft() => NotifyAll();

    private static string FormatNetwork(ExperimentalSandboxNetworkMode mode) =>
        mode switch
        {
            ExperimentalSandboxNetworkMode.Allowed => "direct network",
            ExperimentalSandboxNetworkMode.Proxy => "proxy network",
            _ => "network blocked"
        };

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(StateBadge));
        OnPropertyChanged(nameof(MemberCountText));
        OnPropertyChanged(nameof(PolicySummary));
        OnPropertyChanged(nameof(IdentitySummary));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class ExperimentalSandboxDraft
{
    internal string Name { get; set; } = string.Empty;

    internal bool UseAppContainer { get; set; } = true;

    internal int IntegrityIndex { get; set; }

    internal bool LeastPrivilege { get; set; }

    internal bool DisallowWin32k { get; set; }

    internal bool BlockExternalHandles { get; set; }

    internal bool BlockClipboardRead { get; set; }

    internal bool BlockClipboardWrite { get; set; }

    internal bool BlockSystemParameters { get; set; }

    internal bool BlockDisplaySettings { get; set; }

    internal bool BlockGlobalAtoms { get; set; }

    internal bool BlockDesktop { get; set; }

    internal bool BlockExitWindows { get; set; }

    internal bool BlockIme { get; set; }

    internal bool BlockInputInjection { get; set; }

    internal int NetworkModeIndex { get; set; }

    internal string ProxyUrl { get; set; } = string.Empty;

    internal bool InternetClient { get; set; }

    internal bool InternetClientServer { get; set; }

    internal bool PrivateNetwork { get; set; }

    internal bool RegistryRead { get; set; }

    internal string CustomCapabilities { get; set; } = string.Empty;

    internal bool UseMinimalEnvironment { get; set; }

    internal string ExecutablePath { get; set; } = string.Empty;

    internal string Arguments { get; set; } = string.Empty;

    internal string WorkingDirectory { get; set; } = string.Empty;

    internal bool IncludeTargetAccess { get; set; } = true;

    internal bool IncludeWorkingDirectoryWriteAccess { get; set; } = true;

    internal ObservableCollection<ExperimentalSandboxFileRuleDraft> FileRules { get; } = [];

    internal void Reset(string name)
    {
        Name = name;
        UseAppContainer = true;
        IntegrityIndex = 0;
        LeastPrivilege = false;
        DisallowWin32k = false;
        BlockExternalHandles = false;
        BlockClipboardRead = false;
        BlockClipboardWrite = false;
        BlockSystemParameters = false;
        BlockDisplaySettings = false;
        BlockGlobalAtoms = false;
        BlockDesktop = false;
        BlockExitWindows = false;
        BlockIme = false;
        BlockInputInjection = false;
        NetworkModeIndex = 0;
        ProxyUrl = string.Empty;
        InternetClient = false;
        InternetClientServer = false;
        PrivateNetwork = false;
        RegistryRead = false;
        CustomCapabilities = string.Empty;
        UseMinimalEnvironment = false;
        ExecutablePath = string.Empty;
        Arguments = string.Empty;
        WorkingDirectory = string.Empty;
        IncludeTargetAccess = true;
        IncludeWorkingDirectoryWriteAccess = true;
        FileRules.Clear();
    }
}

internal sealed class ExperimentalSandboxFileRuleDraft : INotifyPropertyChanged
{
    private int _accessIndex;

    internal ExperimentalSandboxFileRuleDraft(string path, int accessIndex)
    {
        Path = path;
        _accessIndex = accessIndex;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; }

    public int AccessIndex
    {
        get => _accessIndex;
        set
        {
            if (_accessIndex == value)
            {
                return;
            }

            _accessIndex = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(AccessSummary)));
        }
    }

    public string AccessSummary =>
        AccessIndex switch
        {
            1 => "Read only",
            2 => "Deny",
            _ => "Read and write"
        };
}
