using System.Globalization;
using System.Runtime.CompilerServices;
using Shackles.App.Infrastructure;
using Shackles.App.Models;

namespace Shackles.App.ViewModels;

internal sealed class RestrictionEditorViewModel : ObservableObject
{
    private bool _loading;
    private readonly bool _canPostEndOfJobNotification;
    private bool _isDirty;
    private string _validationMessage = string.Empty;
    private LoadedTextValue<TimeSpan> _loadedPerProcessTime;
    private LoadedTextValue<TimeSpan> _loadedPerJobTime;
    private LoadedTextValue<ulong> _loadedMinimumWorkingSet;
    private LoadedTextValue<ulong> _loadedMaximumWorkingSet;
    private LoadedTextValue<ulong> _loadedProcessMemory;
    private LoadedTextValue<ulong> _loadedJobMemory;
    private LoadedTextValue<double> _loadedNetworkBandwidth;
    private ulong? _loadedExactNetworkBandwidthBytes;
    private LoadedTextValue<TimeSpan> _loadedNotifyJobTime;
    private LoadedTextValue<ulong> _loadedNotifyJobMemory;
    private LoadedTextValue<ulong> _loadedNotifyLowMemory;
    private LoadedTextValue<ulong> _loadedNotifyIoRead;
    private LoadedTextValue<ulong> _loadedNotifyIoWrite;

    private bool _killOnJobClose;
    private bool _breakawayAllowed;
    private bool _silentBreakawayAllowed;
    private bool _dieOnUnhandledException;
    private bool _activeProcessLimitEnabled;
    private string _activeProcessLimit = "1";
    private bool _perProcessTimeEnabled;
    private string _perProcessTimeSeconds = "60";
    private bool _perJobTimeEnabled;
    private string _perJobTimeSeconds = "300";
    private bool _workingSetEnabled;
    private string _minimumWorkingSetMb = "16";
    private string _maximumWorkingSetMb = "512";
    private bool _processMemoryEnabled;
    private string _processMemoryMb = "512";
    private bool _jobMemoryEnabled;
    private string _jobMemoryMb = "1024";
    private bool _affinityEnabled;
    private string _affinityMask = "0x1";
    private bool _subsetAffinityAllowed;
    private bool _priorityClassEnabled;
    private ProcessPriorityChoice _priorityClass = ProcessPriorityChoice.Normal;
    private bool _schedulingClassEnabled;
    private string _schedulingClass = "5";

    private CpuControlMode _cpuMode;
    private string _cpuRatePercent = "25";
    private string _cpuWeight = "5";
    private string _cpuMinimumPercent = "10";
    private string _cpuMaximumPercent = "50";
    private bool _cpuNotify;
    private bool _usesUnsupportedPerProcessorCaps;

    private bool _networkBandwidthEnabled;
    private string _networkBandwidthMbps = "10";
    private bool _dscpEnabled;
    private string _dscpTag = "0";

    private bool _restrictHandles;
    private bool _restrictReadClipboard;
    private bool _restrictWriteClipboard;
    private bool _restrictSystemParameters;
    private bool _restrictDisplaySettings;
    private bool _restrictGlobalAtoms;
    private bool _restrictDesktops;
    private bool _restrictExitWindows;
    private bool _restrictIme;
    private bool _restrictInjection;

    private bool _notifyJobTimeEnabled;
    private string _notifyJobTimeSeconds = "300";
    private bool _notifyJobMemoryEnabled;
    private string _notifyJobMemoryMb = "1024";
    private bool _notifyLowMemoryEnabled;
    private string _notifyLowMemoryMb = "128";
    private bool _notifyIoReadEnabled;
    private string _notifyIoReadMb = "1024";
    private bool _notifyIoWriteEnabled;
    private string _notifyIoWriteMb = "1024";
    private RateTolerance _cpuTolerance;
    private RateToleranceInterval _cpuToleranceInterval;
    private RateTolerance _ioTolerance;
    private RateToleranceInterval _ioToleranceInterval;
    private RateTolerance _networkTolerance;
    private RateToleranceInterval _networkToleranceInterval;

    private string _processorGroups = string.Empty;
    private JobEndAction _endAction = JobEndAction.TerminateAtEndOfJob;

    public event EventHandler? DraftChanged;

    public IReadOnlyList<ProcessPriorityChoice> PriorityChoices { get; } = Enum.GetValues<ProcessPriorityChoice>();
    public IReadOnlyList<CpuControlMode> CpuModes { get; } = Enum.GetValues<CpuControlMode>();
    public IReadOnlyList<RateTolerance> ToleranceChoices { get; } = Enum.GetValues<RateTolerance>();
    public IReadOnlyList<RateToleranceInterval> ToleranceIntervalChoices { get; } = Enum.GetValues<RateToleranceInterval>();
    public RestrictionEditorViewModel(bool canPostEndOfJobNotification)
    {
        _canPostEndOfJobNotification = canPostEndOfJobNotification;
        EndActionChoices = canPostEndOfJobNotification
            ? Enum.GetValues<JobEndAction>()
            : [JobEndAction.TerminateAtEndOfJob];
    }

    public IReadOnlyList<JobEndAction> EndActionChoices { get; private set; }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationMessage));
            }
        }
    }

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public bool KillOnJobClose { get => _killOnJobClose; set => SetDraft(ref _killOnJobClose, value); }
    public bool BreakawayAllowed { get => _breakawayAllowed; set => SetDraft(ref _breakawayAllowed, value); }
    public bool SilentBreakawayAllowed { get => _silentBreakawayAllowed; set => SetDraft(ref _silentBreakawayAllowed, value); }
    public bool DieOnUnhandledException { get => _dieOnUnhandledException; set => SetDraft(ref _dieOnUnhandledException, value); }
    public bool ActiveProcessLimitEnabled { get => _activeProcessLimitEnabled; set => SetDraft(ref _activeProcessLimitEnabled, value); }
    public string ActiveProcessLimit { get => _activeProcessLimit; set => SetDraft(ref _activeProcessLimit, value); }
    public bool PerProcessTimeEnabled { get => _perProcessTimeEnabled; set => SetDraft(ref _perProcessTimeEnabled, value); }
    public string PerProcessTimeSeconds { get => _perProcessTimeSeconds; set => SetDraft(ref _perProcessTimeSeconds, value); }
    public bool PerJobTimeEnabled { get => _perJobTimeEnabled; set => SetDraft(ref _perJobTimeEnabled, value); }
    public string PerJobTimeSeconds { get => _perJobTimeSeconds; set => SetDraft(ref _perJobTimeSeconds, value); }
    public bool WorkingSetEnabled { get => _workingSetEnabled; set => SetDraft(ref _workingSetEnabled, value); }
    public string MinimumWorkingSetMb { get => _minimumWorkingSetMb; set => SetDraft(ref _minimumWorkingSetMb, value); }
    public string MaximumWorkingSetMb { get => _maximumWorkingSetMb; set => SetDraft(ref _maximumWorkingSetMb, value); }
    public bool ProcessMemoryEnabled { get => _processMemoryEnabled; set => SetDraft(ref _processMemoryEnabled, value); }
    public string ProcessMemoryMb { get => _processMemoryMb; set => SetDraft(ref _processMemoryMb, value); }
    public bool JobMemoryEnabled { get => _jobMemoryEnabled; set => SetDraft(ref _jobMemoryEnabled, value); }
    public string JobMemoryMb { get => _jobMemoryMb; set => SetDraft(ref _jobMemoryMb, value); }
    public bool AffinityEnabled { get => _affinityEnabled; set => SetDraft(ref _affinityEnabled, value); }
    public string AffinityMask { get => _affinityMask; set => SetDraft(ref _affinityMask, value); }
    public bool SubsetAffinityAllowed { get => _subsetAffinityAllowed; set => SetDraft(ref _subsetAffinityAllowed, value); }
    public bool PriorityClassEnabled { get => _priorityClassEnabled; set => SetDraft(ref _priorityClassEnabled, value); }
    public ProcessPriorityChoice PriorityClass { get => _priorityClass; set => SetDraft(ref _priorityClass, value); }
    public bool SchedulingClassEnabled { get => _schedulingClassEnabled; set => SetDraft(ref _schedulingClassEnabled, value); }
    public string SchedulingClass { get => _schedulingClass; set => SetDraft(ref _schedulingClass, value); }

    public CpuControlMode CpuMode { get => _cpuMode; set { if (SetDraft(ref _cpuMode, value)) OnPropertyChanged(nameof(IsCpuEnabled)); } }
    public bool IsCpuEnabled => CpuMode != CpuControlMode.Disabled;
    public string CpuRatePercent { get => _cpuRatePercent; set => SetDraft(ref _cpuRatePercent, value); }
    public string CpuWeight { get => _cpuWeight; set => SetDraft(ref _cpuWeight, value); }
    public string CpuMinimumPercent { get => _cpuMinimumPercent; set => SetDraft(ref _cpuMinimumPercent, value); }
    public string CpuMaximumPercent { get => _cpuMaximumPercent; set => SetDraft(ref _cpuMaximumPercent, value); }
    public bool CpuNotify { get => _cpuNotify; set => SetDraft(ref _cpuNotify, value); }
    public bool UsesUnsupportedPerProcessorCaps
    {
        get => _usesUnsupportedPerProcessorCaps;
        private set
        {
            if (SetProperty(ref _usesUnsupportedPerProcessorCaps, value))
            {
                OnPropertyChanged(nameof(CanEditCpu));
            }
        }
    }
    public bool CanEditCpu => !UsesUnsupportedPerProcessorCaps;

    public bool NetworkBandwidthEnabled { get => _networkBandwidthEnabled; set => SetDraft(ref _networkBandwidthEnabled, value); }
    public string NetworkBandwidthMbps { get => _networkBandwidthMbps; set => SetDraft(ref _networkBandwidthMbps, value); }
    public bool DscpEnabled { get => _dscpEnabled; set => SetDraft(ref _dscpEnabled, value); }
    public string DscpTag { get => _dscpTag; set => SetDraft(ref _dscpTag, value); }

    public bool RestrictHandles { get => _restrictHandles; set => SetDraft(ref _restrictHandles, value); }
    public bool RestrictReadClipboard { get => _restrictReadClipboard; set => SetDraft(ref _restrictReadClipboard, value); }
    public bool RestrictWriteClipboard { get => _restrictWriteClipboard; set => SetDraft(ref _restrictWriteClipboard, value); }
    public bool RestrictSystemParameters { get => _restrictSystemParameters; set => SetDraft(ref _restrictSystemParameters, value); }
    public bool RestrictDisplaySettings { get => _restrictDisplaySettings; set => SetDraft(ref _restrictDisplaySettings, value); }
    public bool RestrictGlobalAtoms { get => _restrictGlobalAtoms; set => SetDraft(ref _restrictGlobalAtoms, value); }
    public bool RestrictDesktops { get => _restrictDesktops; set => SetDraft(ref _restrictDesktops, value); }
    public bool RestrictExitWindows { get => _restrictExitWindows; set => SetDraft(ref _restrictExitWindows, value); }
    public bool RestrictIme { get => _restrictIme; set => SetDraft(ref _restrictIme, value); }
    public bool RestrictInjection { get => _restrictInjection; set => SetDraft(ref _restrictInjection, value); }

    public bool NotifyJobTimeEnabled { get => _notifyJobTimeEnabled; set => SetDraft(ref _notifyJobTimeEnabled, value); }
    public string NotifyJobTimeSeconds { get => _notifyJobTimeSeconds; set => SetDraft(ref _notifyJobTimeSeconds, value); }
    public bool NotifyJobMemoryEnabled { get => _notifyJobMemoryEnabled; set => SetDraft(ref _notifyJobMemoryEnabled, value); }
    public string NotifyJobMemoryMb { get => _notifyJobMemoryMb; set => SetDraft(ref _notifyJobMemoryMb, value); }
    public bool NotifyLowMemoryEnabled { get => _notifyLowMemoryEnabled; set => SetDraft(ref _notifyLowMemoryEnabled, value); }
    public string NotifyLowMemoryMb { get => _notifyLowMemoryMb; set => SetDraft(ref _notifyLowMemoryMb, value); }
    public bool NotifyIoReadEnabled { get => _notifyIoReadEnabled; set => SetDraft(ref _notifyIoReadEnabled, value); }
    public string NotifyIoReadMb { get => _notifyIoReadMb; set => SetDraft(ref _notifyIoReadMb, value); }
    public bool NotifyIoWriteEnabled { get => _notifyIoWriteEnabled; set => SetDraft(ref _notifyIoWriteEnabled, value); }
    public string NotifyIoWriteMb { get => _notifyIoWriteMb; set => SetDraft(ref _notifyIoWriteMb, value); }
    public RateTolerance CpuTolerance { get => _cpuTolerance; set => SetDraft(ref _cpuTolerance, value); }
    public RateToleranceInterval CpuToleranceInterval { get => _cpuToleranceInterval; set => SetDraft(ref _cpuToleranceInterval, value); }
    public RateTolerance IoTolerance { get => _ioTolerance; set => SetDraft(ref _ioTolerance, value); }
    public RateToleranceInterval IoToleranceInterval { get => _ioToleranceInterval; set => SetDraft(ref _ioToleranceInterval, value); }
    public RateTolerance NetworkTolerance { get => _networkTolerance; set => SetDraft(ref _networkTolerance, value); }
    public RateToleranceInterval NetworkToleranceInterval { get => _networkToleranceInterval; set => SetDraft(ref _networkToleranceInterval, value); }

    public string ProcessorGroups { get => _processorGroups; set => SetDraft(ref _processorGroups, value); }
    public JobEndAction EndAction { get => _endAction; set => SetDraft(ref _endAction, value); }

    public void Load(RestrictionProfile profile)
    {
        _loading = true;
        try
        {
            var hard = profile.HardLimits;
            KillOnJobClose = hard.KillOnJobClose;
            BreakawayAllowed = hard.BreakawayAllowed;
            SilentBreakawayAllowed = hard.SilentBreakawayAllowed;
            DieOnUnhandledException = hard.DieOnUnhandledException;
            ActiveProcessLimitEnabled = hard.ActiveProcessLimit.HasValue;
            ActiveProcessLimit = Format(hard.ActiveProcessLimit, "1");
            PerProcessTimeEnabled = hard.PerProcessUserTimeLimit.HasValue;
            PerProcessTimeSeconds = FormatSeconds(hard.PerProcessUserTimeLimit, "60");
            PerJobTimeEnabled = hard.PerJobUserTimeLimit.HasValue;
            PerJobTimeSeconds = FormatSeconds(hard.PerJobUserTimeLimit, "300");
            WorkingSetEnabled = hard.MinimumWorkingSetBytes.HasValue || hard.MaximumWorkingSetBytes.HasValue;
            MinimumWorkingSetMb = FormatMebibytes(hard.MinimumWorkingSetBytes, "16");
            MaximumWorkingSetMb = FormatMebibytes(hard.MaximumWorkingSetBytes, "512");
            ProcessMemoryEnabled = hard.ProcessMemoryLimitBytes.HasValue;
            ProcessMemoryMb = FormatMebibytes(hard.ProcessMemoryLimitBytes, "512");
            JobMemoryEnabled = hard.JobMemoryLimitBytes.HasValue;
            JobMemoryMb = FormatMebibytes(hard.JobMemoryLimitBytes, "1024");
            AffinityEnabled = hard.AffinityMask.HasValue;
            AffinityMask = hard.AffinityMask.HasValue ? $"0x{hard.AffinityMask.Value:X}" : "0x1";
            SubsetAffinityAllowed = hard.SubsetAffinityAllowed;
            PriorityClassEnabled = hard.PriorityClass.HasValue;
            PriorityClass = hard.PriorityClass ?? ProcessPriorityChoice.Normal;
            SchedulingClassEnabled = hard.SchedulingClass.HasValue;
            SchedulingClass = Format(hard.SchedulingClass, "5");

            CpuMode = profile.Cpu.Mode;
            CpuRatePercent = Format(profile.Cpu.RatePercent, "25");
            CpuWeight = Format(profile.Cpu.Weight, "5");
            CpuMinimumPercent = Format(profile.Cpu.MinimumPercent, "10");
            CpuMaximumPercent = Format(profile.Cpu.MaximumPercent, "50");
            CpuNotify = profile.Cpu.Notify;
            UsesUnsupportedPerProcessorCaps = profile.Cpu.UsesUnsupportedPerProcessorCaps;

            var displayedBandwidth = profile.Network.MaximumBandwidthMegabitsPerSecond ??
                (profile.Network.ExactMaximumBandwidthBytesPerSecond.HasValue
                    ? profile.Network.ExactMaximumBandwidthBytesPerSecond.Value * 8d / 1_000_000d
                    : null);
            NetworkBandwidthEnabled = displayedBandwidth.HasValue;
            NetworkBandwidthMbps = Format(displayedBandwidth, "10");
            DscpEnabled = profile.Network.DscpTag.HasValue;
            DscpTag = Format(profile.Network.DscpTag, "0");

            var ui = profile.UiRestrictions;
            RestrictHandles = ui.HasFlag(UiRestrictionFlags.Handles);
            RestrictReadClipboard = ui.HasFlag(UiRestrictionFlags.ReadClipboard);
            RestrictWriteClipboard = ui.HasFlag(UiRestrictionFlags.WriteClipboard);
            RestrictSystemParameters = ui.HasFlag(UiRestrictionFlags.SystemParameters);
            RestrictDisplaySettings = ui.HasFlag(UiRestrictionFlags.DisplaySettings);
            RestrictGlobalAtoms = ui.HasFlag(UiRestrictionFlags.GlobalAtoms);
            RestrictDesktops = ui.HasFlag(UiRestrictionFlags.Desktops);
            RestrictExitWindows = ui.HasFlag(UiRestrictionFlags.ExitWindows);
            RestrictIme = ui.HasFlag(UiRestrictionFlags.Ime);
            RestrictInjection = ui.HasFlag(UiRestrictionFlags.Injection);

            var notification = profile.Notifications;
            NotifyJobTimeEnabled = notification.PerJobUserTime.HasValue;
            NotifyJobTimeSeconds = FormatSeconds(notification.PerJobUserTime, "300");
            NotifyJobMemoryEnabled = notification.JobMemoryBytes.HasValue;
            NotifyJobMemoryMb = FormatMebibytes(notification.JobMemoryBytes, "1024");
            NotifyLowMemoryEnabled = notification.JobLowMemoryBytes.HasValue;
            NotifyLowMemoryMb = FormatMebibytes(notification.JobLowMemoryBytes, "128");
            NotifyIoReadEnabled = notification.IoReadBytes.HasValue;
            NotifyIoReadMb = FormatMebibytes(notification.IoReadBytes, "1024");
            NotifyIoWriteEnabled = notification.IoWriteBytes.HasValue;
            NotifyIoWriteMb = FormatMebibytes(notification.IoWriteBytes, "1024");
            CpuTolerance = notification.CpuTolerance;
            CpuToleranceInterval = notification.CpuToleranceInterval;
            IoTolerance = notification.IoTolerance;
            IoToleranceInterval = notification.IoToleranceInterval;
            NetworkTolerance = notification.NetworkTolerance;
            NetworkToleranceInterval = notification.NetworkToleranceInterval;

            ProcessorGroups = string.Join(", ", profile.ProcessorGroups.Select(item => $"{item.Group}:0x{item.Mask:X}"));
            EndAction = profile.EndAction;
            if (!_canPostEndOfJobNotification && profile.EndAction == JobEndAction.PostNotification)
            {
                EndActionChoices = Enum.GetValues<JobEndAction>();
                OnPropertyChanged(nameof(EndActionChoices));
            }

            _loadedPerProcessTime = new(PerProcessTimeSeconds, hard.PerProcessUserTimeLimit, PerProcessTimeEnabled);
            _loadedPerJobTime = new(PerJobTimeSeconds, hard.PerJobUserTimeLimit, PerJobTimeEnabled);
            _loadedMinimumWorkingSet = new(MinimumWorkingSetMb, hard.MinimumWorkingSetBytes, WorkingSetEnabled);
            _loadedMaximumWorkingSet = new(MaximumWorkingSetMb, hard.MaximumWorkingSetBytes, WorkingSetEnabled);
            _loadedProcessMemory = new(ProcessMemoryMb, hard.ProcessMemoryLimitBytes, ProcessMemoryEnabled);
            _loadedJobMemory = new(JobMemoryMb, hard.JobMemoryLimitBytes, JobMemoryEnabled);
            _loadedNetworkBandwidth = new(NetworkBandwidthMbps, displayedBandwidth, NetworkBandwidthEnabled);
            _loadedExactNetworkBandwidthBytes = profile.Network.ExactMaximumBandwidthBytesPerSecond;
            _loadedNotifyJobTime = new(NotifyJobTimeSeconds, notification.PerJobUserTime, NotifyJobTimeEnabled);
            _loadedNotifyJobMemory = new(NotifyJobMemoryMb, notification.JobMemoryBytes, NotifyJobMemoryEnabled);
            _loadedNotifyLowMemory = new(NotifyLowMemoryMb, notification.JobLowMemoryBytes, NotifyLowMemoryEnabled);
            _loadedNotifyIoRead = new(NotifyIoReadMb, notification.IoReadBytes, NotifyIoReadEnabled);
            _loadedNotifyIoWrite = new(NotifyIoWriteMb, notification.IoWriteBytes, NotifyIoWriteEnabled);
            ValidationMessage = string.Empty;
            IsDirty = false;
        }
        finally
        {
            _loading = false;
            DraftChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool TryBuild(out RestrictionProfile profile)
    {
        profile = RestrictionProfile.Empty;
        try
        {
            ulong? minimumWorkingSet = WorkingSetEnabled
                ? PreserveOrParse(MinimumWorkingSetMb, _loadedMinimumWorkingSet, () => ParseMebibytes(MinimumWorkingSetMb, "Minimum working set"))
                : null;
            ulong? maximumWorkingSet = WorkingSetEnabled
                ? PreserveOrParse(MaximumWorkingSetMb, _loadedMaximumWorkingSet, () => ParseMebibytes(MaximumWorkingSetMb, "Maximum working set"))
                : null;
            if (minimumWorkingSet > maximumWorkingSet)
            {
                throw new ValidationException("Minimum working set cannot exceed maximum working set.");
            }

            if (SubsetAffinityAllowed && !AffinityEnabled)
            {
                throw new ValidationException("Subset affinity can only be enabled when a job affinity mask is configured.");
            }

            if (EndAction == JobEndAction.PostNotification && !_canPostEndOfJobNotification)
            {
                throw new ValidationException("PostNotification requires an owned live completion port. Choose TerminateAtEndOfJob or create a new job in Shackles.");
            }

            var hard = new HardLimitSettings(
                KillOnJobClose,
                BreakawayAllowed,
                SilentBreakawayAllowed,
                DieOnUnhandledException,
                ActiveProcessLimitEnabled ? ParseUInt(ActiveProcessLimit, "Active process limit", 1, uint.MaxValue) : null,
                PerProcessTimeEnabled
                    ? PreserveOrParse(PerProcessTimeSeconds, _loadedPerProcessTime, () => ParseSeconds(PerProcessTimeSeconds, "Per-process user time"))
                    : null,
                PerJobTimeEnabled
                    ? PreserveOrParse(PerJobTimeSeconds, _loadedPerJobTime, () => ParseSeconds(PerJobTimeSeconds, "Per-job user time"))
                    : null,
                minimumWorkingSet,
                maximumWorkingSet,
                ProcessMemoryEnabled
                    ? PreserveOrParse(ProcessMemoryMb, _loadedProcessMemory, () => ParseMebibytes(ProcessMemoryMb, "Process memory limit"))
                    : null,
                JobMemoryEnabled
                    ? PreserveOrParse(JobMemoryMb, _loadedJobMemory, () => ParseMebibytes(JobMemoryMb, "Job memory limit"))
                    : null,
                AffinityEnabled ? ParseAffinity(AffinityMask) : null,
                SubsetAffinityAllowed,
                PriorityClassEnabled ? PriorityClass : null,
                SchedulingClassEnabled ? ParseUInt(SchedulingClass, "Scheduling class", 0, 9) : null);

            var cpu = BuildCpuSettings();
            var networkBandwidth = NetworkBandwidthEnabled
                ? PreserveOrParse(
                    NetworkBandwidthMbps,
                    _loadedNetworkBandwidth,
                    () => ParseDouble(NetworkBandwidthMbps, "Maximum outbound bandwidth", 0.000001, 68_719_476_736d))
                : null;
            var exactNetworkBandwidthBytes = NetworkBandwidthEnabled &&
                IsLoadedTextUnchanged(NetworkBandwidthMbps, _loadedNetworkBandwidth)
                    ? _loadedExactNetworkBandwidthBytes
                    : null;
            var network = new NetworkControlSettings(
                networkBandwidth,
                DscpEnabled ? (byte)ParseUInt(DscpTag, "DSCP tag", 0, 63) : null,
                exactNetworkBandwidthBytes);

            var ui = UiRestrictionFlags.None;
            if (RestrictHandles) ui |= UiRestrictionFlags.Handles;
            if (RestrictReadClipboard) ui |= UiRestrictionFlags.ReadClipboard;
            if (RestrictWriteClipboard) ui |= UiRestrictionFlags.WriteClipboard;
            if (RestrictSystemParameters) ui |= UiRestrictionFlags.SystemParameters;
            if (RestrictDisplaySettings) ui |= UiRestrictionFlags.DisplaySettings;
            if (RestrictGlobalAtoms) ui |= UiRestrictionFlags.GlobalAtoms;
            if (RestrictDesktops) ui |= UiRestrictionFlags.Desktops;
            if (RestrictExitWindows) ui |= UiRestrictionFlags.ExitWindows;
            if (RestrictIme) ui |= UiRestrictionFlags.Ime;
            if (RestrictInjection) ui |= UiRestrictionFlags.Injection;

            ValidateTolerancePair(CpuTolerance, CpuToleranceInterval, "CPU");
            ValidateTolerancePair(IoTolerance, IoToleranceInterval, "I/O");
            ValidateTolerancePair(NetworkTolerance, NetworkToleranceInterval, "Network");

            var notifications = new NotificationSettings(
                NotifyJobTimeEnabled
                    ? PreserveOrParse(NotifyJobTimeSeconds, _loadedNotifyJobTime, () => ParseSeconds(NotifyJobTimeSeconds, "Job-time notification threshold"))
                    : null,
                NotifyJobMemoryEnabled
                    ? PreserveOrParse(NotifyJobMemoryMb, _loadedNotifyJobMemory, () => ParseMebibytes(NotifyJobMemoryMb, "Job-memory notification threshold"))
                    : null,
                NotifyLowMemoryEnabled
                    ? PreserveOrParse(NotifyLowMemoryMb, _loadedNotifyLowMemory, () => ParseMebibytes(NotifyLowMemoryMb, "Low-memory notification threshold"))
                    : null,
                NotifyIoReadEnabled
                    ? PreserveOrParse(NotifyIoReadMb, _loadedNotifyIoRead, () => ParseMebibytes(NotifyIoReadMb, "I/O read notification threshold"))
                    : null,
                NotifyIoWriteEnabled
                    ? PreserveOrParse(NotifyIoWriteMb, _loadedNotifyIoWrite, () => ParseMebibytes(NotifyIoWriteMb, "I/O write notification threshold"))
                    : null,
                CpuTolerance,
                CpuToleranceInterval,
                IoTolerance,
                IoToleranceInterval,
                NetworkTolerance,
                NetworkToleranceInterval);

            profile = new RestrictionProfile(hard, cpu, network, ui, notifications, ParseGroups(ProcessorGroups), EndAction);
            ValidationMessage = string.Empty;
            return true;
        }
        catch (ValidationException ex)
        {
            ValidationMessage = ex.Message;
            return false;
        }
        catch (OverflowException)
        {
            ValidationMessage = "One or more values are too large for the Windows job API.";
            return false;
        }
    }

    public void MarkApplied(RestrictionProfile appliedProfile) => Load(appliedProfile);

    private CpuControlSettings BuildCpuSettings()
    {
        if (UsesUnsupportedPerProcessorCaps)
        {
            return new CpuControlSettings(UsesUnsupportedPerProcessorCaps: true);
        }

        return CpuMode switch
        {
            CpuControlMode.Disabled => new CpuControlSettings(),
            CpuControlMode.Rate => new CpuControlSettings(
                CpuMode,
                ParseDouble(CpuRatePercent, "CPU rate", 0.01, 100),
                Notify: CpuNotify),
            CpuControlMode.HardCap => new CpuControlSettings(
                CpuMode,
                ParseDouble(CpuRatePercent, "CPU hard cap", 0.01, 100),
                Notify: CpuNotify),
            CpuControlMode.Weight => new CpuControlSettings(
                CpuMode,
                Weight: ParseUInt(CpuWeight, "CPU weight", 1, 9),
                Notify: CpuNotify),
            CpuControlMode.MinimumMaximum => BuildCpuMinimumMaximum(),
            _ => throw new ValidationException("Select a valid CPU control mode.")
        };
    }

    private CpuControlSettings BuildCpuMinimumMaximum()
    {
        var minimum = ParseDouble(CpuMinimumPercent, "Minimum CPU rate", 0.01, 100);
        var maximum = ParseDouble(CpuMaximumPercent, "Maximum CPU rate", 0.01, 100);
        if (minimum > maximum)
        {
            throw new ValidationException("Minimum CPU rate cannot exceed maximum CPU rate.");
        }

        return new CpuControlSettings(CpuMode, MinimumPercent: minimum, MaximumPercent: maximum, Notify: CpuNotify);
    }

    private bool SetDraft<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        if (!_loading)
        {
            IsDirty = true;
            ValidationMessage = string.Empty;
            DraftChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    private static string Format<T>(T? value, string fallback) where T : struct, IFormattable =>
        value?.ToString(null, CultureInfo.CurrentCulture) ?? fallback;

    private static string FormatSeconds(TimeSpan? value, string fallback) =>
        value?.TotalSeconds.ToString("0.###", CultureInfo.CurrentCulture) ?? fallback;

    private static string FormatMebibytes(ulong? value, string fallback) =>
        value.HasValue ? (value.Value / 1024d / 1024d).ToString("0.###", CultureInfo.CurrentCulture) : fallback;

    private static T? PreserveOrParse<T>(
        string currentText,
        LoadedTextValue<T> loaded,
        Func<T> parse) where T : struct =>
        IsLoadedTextUnchanged(currentText, loaded) ? loaded.Value : parse();

    private static bool IsLoadedTextUnchanged<T>(
        string currentText,
        LoadedTextValue<T> loaded) where T : struct =>
        loaded.WasEnabled && string.Equals(currentText, loaded.Text, StringComparison.Ordinal);

    private static uint ParseUInt(string value, string label, uint minimum, uint maximum)
    {
        if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var result) || result < minimum || result > maximum)
        {
            throw new ValidationException($"{label} must be a whole number from {minimum:N0} to {maximum:N0}.");
        }

        return result;
    }

    private static double ParseDouble(string value, string label, double minimum, double maximum)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var result) ||
            double.IsNaN(result) || double.IsInfinity(result) || result < minimum || result > maximum)
        {
            throw new ValidationException($"{label} must be a number from {minimum:N2} to {maximum:N2}.");
        }

        return result;
    }

    private static TimeSpan ParseSeconds(string value, string label)
    {
        var seconds = ParseDouble(value, label, 0.001, TimeSpan.MaxValue.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private static ulong ParseMebibytes(string value, string label)
    {
        var mebibytes = ParseDouble(value, label, 0.000001, ulong.MaxValue / 1024d / 1024d);
        return checked((ulong)Math.Round(mebibytes * 1024d * 1024d, MidpointRounding.AwayFromZero));
    }

    private static ulong ParseAffinity(string value)
    {
        var text = value.Trim();
        var style = NumberStyles.Integer;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
            style = NumberStyles.AllowHexSpecifier;
        }

        if (!ulong.TryParse(text, style, CultureInfo.InvariantCulture, out var mask) || mask == 0)
        {
            throw new ValidationException("Affinity mask must be a non-zero decimal value or hexadecimal value beginning with 0x.");
        }

        return mask;
    }

    private static ProcessorGroupAffinity[] ParseGroups(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<ProcessorGroupAffinity>();
        }

        var groups = new SortedDictionary<ushort, ulong>();
        foreach (var token in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = token.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !ushort.TryParse(parts[0], NumberStyles.Integer, CultureInfo.CurrentCulture, out var group))
            {
                throw new ValidationException($"Processor affinity '{token}' must use group:mask format, for example 0:0xFF.");
            }

            var mask = ParseAffinity(parts[1]);
            if (!groups.TryAdd(group, mask))
            {
                throw new ValidationException($"Processor group {group} is listed more than once.");
            }
        }

        return groups.Select(item => new ProcessorGroupAffinity(item.Key, item.Value)).ToArray();
    }

    private static void ValidateTolerancePair(RateTolerance tolerance, RateToleranceInterval interval, string label)
    {
        if ((tolerance == RateTolerance.None) != (interval == RateToleranceInterval.None))
        {
            throw new ValidationException($"{label} tolerance and interval must either both be set or both be None.");
        }
    }

    private readonly record struct LoadedTextValue<T>(
        string? Text,
        T? Value,
        bool WasEnabled) where T : struct;

    private sealed class ValidationException(string message) : Exception(message);
}
