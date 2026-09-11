using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Shackles.App.Models;
using Shackles.App.ViewModels;

namespace Shackles.App.Dialogs;

internal sealed record RunningProcessPickerOptions(
    string WindowTitle,
    string Description,
    string SelectButtonText,
    string SelectButtonAutomationName,
    IReadOnlyCollection<ProcessIdentity>? ExcludedIdentities = null,
    bool ExcludeOtherShacklesInstances = false);

public sealed partial class RunningProcessPickerDialog : Window
{
    private readonly MainViewModel _viewModel;
    private readonly RunningProcessPickerOptions _options;
    private readonly HashSet<ProcessIdentity> _excludedIdentities;
    private readonly ObservableCollection<ProcessEntry> _processes = [];
    private readonly ICollectionView _processView;
    private bool _isRefreshing;
    private bool _initialRefreshStarted;

    internal RunningProcessPickerDialog(
        MainViewModel viewModel,
        RunningProcessPickerOptions options)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(options);
        _viewModel = viewModel;
        _options = options;
        _excludedIdentities = options.ExcludedIdentities?.ToHashSet() ?? [];
        InitializeComponent();

        Title = options.WindowTitle;
        DescriptionText.Text = options.Description;
        SelectButton.Content = options.SelectButtonText;
        AutomationProperties.SetName(SelectButton, options.SelectButtonAutomationName);

        _processView = CollectionViewSource.GetDefaultView(_processes);
        _processView.Filter = FilterProcess;
        _processView.SortDescriptions.Add(
            new SortDescription(nameof(ProcessEntry.Name), ListSortDirection.Ascending));
        _processView.SortDescriptions.Add(
            new SortDescription(nameof(ProcessEntry.ProcessId), ListSortDirection.Ascending));
        DataContext = _processView;

        ReloadRows();
        Loaded += Dialog_Loaded;
    }

    internal IReadOnlyList<ProcessEntry> SelectedProcesses { get; private set; } = [];

    private async void Dialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialRefreshStarted)
        {
            return;
        }

        _initialRefreshStarted = true;
        await RefreshProcessesAsync().ConfigureAwait(true);
        SearchTextBox.Focus();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshProcessesAsync().ConfigureAwait(true);

    private async Task RefreshProcessesAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        RefreshButton.IsEnabled = false;
        SelectButton.IsEnabled = false;
        ProcessCountText.Text = "Refreshing running processes…";
        try
        {
            if (await _viewModel.RefreshProcessesAsync().ConfigureAwait(true))
            {
                ReloadRows();
            }
            else
            {
                ProcessCountText.Text = _processes.Count == 0
                    ? "Running processes could not be refreshed."
                    : $"Refresh failed • showing the previous {_processes.Count.ToString(CultureInfo.CurrentCulture)}-process snapshot";
            }
        }
        finally
        {
            _isRefreshing = false;
            RefreshButton.IsEnabled = true;
            UpdateSelectionState();
        }
    }

    private void ReloadRows()
    {
        var selectedIdentities = ProcessList.SelectedItems
            .OfType<ProcessEntry>()
            .Where(process => process.CreationTimeUtcFileTime.HasValue)
            .Select(ToIdentity)
            .ToHashSet();
        var eligible = _viewModel.Processes
            .Where(IsEligible)
            .ToArray();

        _processes.Clear();
        foreach (var process in eligible)
        {
            _processes.Add(process);
        }

        _processView.Refresh();
        foreach (var process in _processes.Where(
                     process => selectedIdentities.Contains(ToIdentity(process))))
        {
            ProcessList.SelectedItems.Add(process);
        }

        var omittedCount = _viewModel.Processes.Count - eligible.Length;
        ProcessCountText.Text = omittedCount == 0
            ? $"{eligible.Length.ToString(CultureInfo.CurrentCulture)} eligible process" +
              (eligible.Length == 1 ? string.Empty : "es")
            : $"{eligible.Length.ToString(CultureInfo.CurrentCulture)} eligible • " +
              $"{omittedCount.ToString(CultureInfo.CurrentCulture)} omitted";
        UpdateSelectionState();
    }

    private bool IsEligible(ProcessEntry process)
    {
        if (!process.IsAssignable ||
            !process.CreationTimeUtcFileTime.HasValue ||
            _excludedIdentities.Contains(ToIdentity(process)))
        {
            return false;
        }

        return !_options.ExcludeOtherShacklesInstances || !IsShacklesProcess(process);
    }

    private static bool IsShacklesProcess(ProcessEntry process)
    {
        var currentImagePath = Environment.ProcessPath;
        var normalizedCurrentPath = TryNormalizePath(currentImagePath);
        var normalizedCandidatePath = TryNormalizePath(process.ImagePath);
        if (normalizedCurrentPath is not null &&
            normalizedCandidatePath is not null &&
            string.Equals(
                normalizedCandidatePath,
                normalizedCurrentPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var currentProcessName = Path.GetFileNameWithoutExtension(currentImagePath);
        return !string.IsNullOrWhiteSpace(currentProcessName) &&
               string.Equals(
                   process.Name,
                   currentProcessName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryNormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static ProcessIdentity ToIdentity(ProcessEntry process) => new(
        process.ProcessId,
        process.CreationTimeUtcFileTime!.Value);

    private bool FilterProcess(object value)
    {
        if (value is not ProcessEntry process ||
            string.IsNullOrWhiteSpace(SearchTextBox.Text))
        {
            return true;
        }

        var query = SearchTextBox.Text.Trim();
        return process.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               process.ProcessId.ToString(CultureInfo.CurrentCulture)
                   .Contains(query, StringComparison.OrdinalIgnoreCase) ||
               (process.ImagePath?.Contains(
                   query,
                   StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _processView?.Refresh();
        UpdateSelectionState();
    }

    private void ProcessList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) => UpdateSelectionState();

    private void UpdateSelectionState()
    {
        if (SelectionCountText is null || SelectButton is null)
        {
            return;
        }

        var selectedCount = ProcessList?.SelectedItems.Count ?? 0;
        SelectionCountText.Text = selectedCount switch
        {
            0 => "No processes selected",
            1 => "1 process selected",
            _ => $"{selectedCount.ToString(CultureInfo.CurrentCulture)} processes selected"
        };
        SelectButton.IsEnabled = !_isRefreshing && selectedCount != 0;
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = ProcessList.SelectedItems
            .OfType<ProcessEntry>()
            .Where(process => process.CreationTimeUtcFileTime.HasValue)
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        SelectedProcesses = selected;
        DialogResult = true;
    }
}
