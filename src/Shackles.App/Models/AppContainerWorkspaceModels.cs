using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Shackles.AppContainers;

namespace Shackles.App.Models;

internal sealed class AppContainerSandboxCard : INotifyPropertyChanged
{
    private AppContainerSnapshot? _snapshot;

    internal AppContainerSandboxCard(string initialName)
    {
        Draft = new AppContainerSandboxDraft();
        Draft.Reset(initialName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal AppContainerSandboxDraft Draft { get; }

    internal AppContainerSandbox? Sandbox { get; private set; }

    internal bool IsActive => Sandbox is not null;

    internal AppContainerSnapshot? Snapshot => _snapshot;

    public string DisplayName
    {
        get
        {
            if (_snapshot is not null)
            {
                return _snapshot.DisplayName;
            }

            var name = Draft.Name.Trim();
            return name.Length > 0 ? name : "Untitled sandbox";
        }
    }

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
            var strict = _snapshot?.Options.IsolationMode ==
                         AppContainerIsolationMode.LowPrivilege ||
                         (!IsActive && Draft.UseLowPrivilege);
            var networkCount = _snapshot?.Options.CapabilityNames.Count(
                IsNetworkCapability) ?? Draft.NetworkCapabilityCount;
            var credentialsEnabled = _snapshot?.Options.CapabilityNames.Any(
                IsNetworkCredentialCapability) ?? Draft.NetworkCredentials;
            return $"{(strict ? "Strict (LPAC)" : "Standard")} • " +
                   (networkCount == 0
                       ? "no network"
                       : $"{networkCount} network grant{(networkCount == 1 ? string.Empty : "s")}") +
                   (credentialsEnabled ? " • network credentials enabled" : string.Empty);
        }
    }

    public string IdentitySummary =>
        _snapshot is null ? "SID allocated on first launch" : _snapshot.Sid;

    internal void Attach(AppContainerSandbox sandbox)
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

    private static bool IsNetworkCapability(string capability) =>
        capability is "internetClient" or
            "internetClientServer" or
            "privateNetworkClientServer";

    private static bool IsNetworkCredentialCapability(string capability) =>
        capability.Equals(
            "enterpriseAuthentication",
            StringComparison.OrdinalIgnoreCase) ||
        capability.Equals(
            "developmentModeNetwork",
            StringComparison.OrdinalIgnoreCase);

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

internal sealed class AppContainerSandboxDraft
{
    internal string Name { get; set; } = string.Empty;

    internal bool UseLowPrivilege { get; set; }

    internal bool AllowChildren { get; set; } = true;

    internal bool UseMinimalEnvironment { get; set; }

    internal string ExecutablePath { get; set; } = string.Empty;

    internal string Arguments { get; set; } = string.Empty;

    internal string WorkingDirectory { get; set; } = string.Empty;

    internal bool IncludeTargetAccess { get; set; } = true;

    internal bool InternetClient { get; set; }

    internal bool InternetServer { get; set; }

    internal bool PrivateNetwork { get; set; }

    internal bool NetworkCredentials { get; set; }

    internal bool PicturesLibrary { get; set; }

    internal bool VideosLibrary { get; set; }

    internal bool MusicLibrary { get; set; }

    internal bool RemovableStorage { get; set; }

    internal ObservableCollection<AppContainerFileGrantDraft> FileGrants { get; } = [];

    internal ObservableCollection<AppContainerRegistryGrantDraft> RegistryGrants { get; } = [];

    internal int NetworkCapabilityCount =>
        (InternetClient ? 1 : 0) +
        (InternetServer ? 1 : 0) +
        (PrivateNetwork ? 1 : 0);

    internal int CuratedCapabilityCount =>
        (PicturesLibrary ? 1 : 0) +
        (VideosLibrary ? 1 : 0) +
        (MusicLibrary ? 1 : 0) +
        (RemovableStorage ? 1 : 0);

    internal void Reset(string name)
    {
        Name = name;
        UseLowPrivilege = false;
        AllowChildren = true;
        UseMinimalEnvironment = false;
        ExecutablePath = string.Empty;
        Arguments = string.Empty;
        WorkingDirectory = string.Empty;
        IncludeTargetAccess = true;
        InternetClient = false;
        InternetServer = false;
        PrivateNetwork = false;
        NetworkCredentials = false;
        PicturesLibrary = false;
        VideosLibrary = false;
        MusicLibrary = false;
        RemovableStorage = false;
        FileGrants.Clear();
        RegistryGrants.Clear();
    }
}

internal sealed class AppContainerFileGrantDraft
{
    internal AppContainerFileGrantDraft(
        string path,
        bool isDirectory,
        int accessIndex)
    {
        Path = path;
        IsDirectory = isDirectory;
        AccessIndex = accessIndex;
    }

    public string Path { get; }

    public bool IsDirectory { get; }

    public string KindText => IsDirectory ? "Folder" : "File";

    public int AccessIndex { get; set; }
}

internal sealed class AppContainerRegistryGrantDraft : INotifyPropertyChanged
{
    private int _accessIndex;
    private int _viewIndex;

    internal AppContainerRegistryGrantDraft(
        string path,
        int accessIndex,
        int viewIndex)
    {
        Path = path;
        _accessIndex = accessIndex;
        _viewIndex = viewIndex;
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        }
    }

    public int ViewIndex
    {
        get => _viewIndex;
        set
        {
            if (_viewIndex == value)
            {
                return;
            }

            _viewIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        }
    }

    public string Summary =>
        $"{(AccessIndex == 0 ? "Read" : "Read and write")} • " +
        (ViewIndex switch
        {
            1 => "32-bit view",
            2 => "64-bit view",
            _ => "Automatic view"
        });
}
