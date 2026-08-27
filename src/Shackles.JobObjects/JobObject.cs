using System.Runtime.InteropServices;
using Shackles.JobObjects.Internal;
using Shackles.JobObjects.Interop;

namespace Shackles.JobObjects;

/// <summary>Owns a Windows job-object handle and provides typed access to documented restrictions.</summary>
public sealed partial class JobObject : IDisposable
{
    private const int ErrorAlreadyExists = 183;
    private const uint ResumeFailed = uint.MaxValue;

    private readonly SafeJobHandle _handle;
    private readonly object _mutationGate = new();
    private bool _disposed;

    private JobObject(SafeJobHandle handle, string? name, bool createdNew, JobAccessRights accessRights)
    {
        _handle = handle;
        Name = name;
        CreatedNew = createdNew;
        AccessRights = accessRights;
        Capabilities = JobCapabilities.Detect(accessRights);
    }

    public string? Name { get; }

    public bool CreatedNew { get; }

    /// <summary>The access mask used to create or open this owned handle.</summary>
    public JobAccessRights AccessRights { get; }

    public JobCapabilities Capabilities { get; }

    /// <summary>Creates an unnamed job, or creates/opens a named job with full access.</summary>
    public static JobObject Create(string? name = null)
    {
        EnsureWindows();
        JobValidation.ValidateName(name, nameof(name));

        var rawHandle = NativeMethods.CreateJobObject(0, name);
        var error = Marshal.GetLastPInvokeError();
        var handle = new SafeJobHandle(rawHandle);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new JobObjectException(JobOperation.CreateJob, error);
        }

        var job = new JobObject(handle, name, createdNew: error != ErrorAlreadyExists, JobAccessRights.FullControl);
        if (job.CreatedNew)
        {
            try
            {
                job.EnableNotificationDelivery();
            }
            catch
            {
                job.Dispose();
                throw;
            }
        }

        return job;
    }

    /// <summary>Opens a named job with the least-privilege rights needed to manage it by default.</summary>
    public static JobObject Open(string name, JobAccessRights access = JobAccessRights.Manage)
    {
        EnsureWindows();
        ArgumentNullException.ThrowIfNull(name);
        JobValidation.ValidateName(name, nameof(name));
        if (access == JobAccessRights.None)
        {
            throw new ArgumentOutOfRangeException(nameof(access), access, "At least one access right is required.");
        }

        if ((access & ~JobAccessRights.FullControl) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(access), access, "The access mask contains unknown job-object rights.");
        }

        var rawHandle = NativeMethods.OpenJobObject(access, inheritHandle: 0, name);
        var error = Marshal.GetLastPInvokeError();
        var handle = new SafeJobHandle(rawHandle);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new JobObjectException(JobOperation.OpenJob, error);
        }

        return new JobObject(handle, name, createdNew: false, access);
    }

    public JobRestrictions GetRestrictions()
    {
        ThrowIfDisposed();
        return new JobRestrictions
        {
            ExtendedLimits = GetExtendedLimits(),
            UiRestrictions = GetUiRestrictions(),
            CpuRateControl = GetCpuRateControl(),
            NetworkRateControl = GetNetworkRateControl(),
            EndOfJobAction = GetEndOfJobAction(),
            NotificationLimits = GetNotificationLimits(),
            ProcessorGroups = GetProcessorGroups()
        };
    }

    /// <summary>
    /// Applies independent native information classes in a stable order. Windows does not provide a
    /// cross-class transaction; if a later call fails, earlier classes remain applied.
    /// </summary>
    public void ApplyRestrictions(JobRestrictions restrictions)
    {
        ArgumentNullException.ThrowIfNull(restrictions);
        ArgumentNullException.ThrowIfNull(restrictions.ExtendedLimits);
        ArgumentNullException.ThrowIfNull(restrictions.CpuRateControl);
        ArgumentNullException.ThrowIfNull(restrictions.NetworkRateControl);
        ArgumentNullException.ThrowIfNull(restrictions.NotificationLimits);
        ArgumentNullException.ThrowIfNull(restrictions.ProcessorGroups);
        ThrowIfDisposed();

        lock (_mutationGate)
        {
            PrevalidateRestrictionsForApply(restrictions);

            // End action must precede a hard per-job time limit. Otherwise a limit that is already
            // exceeded could execute the job's old action before the requested action is installed.
            SetEndOfJobAction(restrictions.EndOfJobAction);
            SetExtendedLimits(restrictions.ExtendedLimits);
            SetUiRestrictions(restrictions.UiRestrictions);
            SetCpuRateControl(restrictions.CpuRateControl);
            SetNetworkRateControl(restrictions.NetworkRateControl);
            SetNotificationLimits(restrictions.NotificationLimits);
            if (restrictions.ProcessorGroups.Count > 0)
            {
                SetProcessorGroups(restrictions.ProcessorGroups);
            }
        }
    }

    private void PrevalidateRestrictionsForApply(JobRestrictions restrictions)
    {
        Validate(restrictions.ExtendedLimits);
        ValidateUiRestrictions(restrictions.UiRestrictions);
        Validate(restrictions.CpuRateControl);
        Validate(restrictions.NetworkRateControl);
        ValidateEndOfJobAction(restrictions.EndOfJobAction);
        Validate(restrictions.NotificationLimits);
        if (restrictions.ProcessorGroups.Count > 0)
        {
            Validate(restrictions.ProcessorGroups);
        }

        var currentCpu = Query<NativeCpuRateControlInformation>(JobObjectInformationClass.CpuRateControlInformation);
        if (FromNative(currentCpu) != restrictions.CpuRateControl)
        {
            EnsureCpuRateControlCanChange(currentCpu, restrictions.CpuRateControl);
        }

        var currentEndAction = Query<NativeEndOfJobTimeInformation>(
            JobObjectInformationClass.EndOfJobTimeInformation).EndOfJobTimeAction;
        if (currentEndAction != restrictions.EndOfJobAction)
        {
            EnsureEndOfJobActionCanChange(restrictions.EndOfJobAction);
        }
    }

    /// <summary>
    /// Samples several independent native information classes sequentially. The result is a useful
    /// point-in-time view, but Windows does not provide a cross-class atomic snapshot.
    /// </summary>
    public JobSnapshot GetSnapshot()
    {
        ThrowIfDisposed();
        var extended = Query<NativeExtendedLimitInformation>(JobObjectInformationClass.ExtendedLimitInformation);
        var restrictions = new JobRestrictions
        {
            ExtendedLimits = FromNative(extended),
            UiRestrictions = GetUiRestrictions(),
            CpuRateControl = GetCpuRateControl(),
            NetworkRateControl = GetNetworkRateControl(),
            EndOfJobAction = GetEndOfJobAction(),
            NotificationLimits = GetNotificationLimits(),
            ProcessorGroups = GetProcessorGroups()
        };

        return new JobSnapshot(
            restrictions,
            GetAccounting(),
            GetProcessIds(),
            new JobMemoryPeaks((ulong)extended.PeakProcessMemoryUsed, (ulong)extended.PeakJobMemoryUsed),
            GetLimitViolations(),
            DateTimeOffset.UtcNow);
    }

    public void Dispose()
    {
        Task? dispatcher;
        CancellationTokenSource? dispatcherCancellation;
        int callbackThreadId;

        lock (_notificationGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopNotificationDeliveryCore();
            (dispatcher, dispatcherCancellation) = StopNotificationDispatcherCore();
            callbackThreadId = Volatile.Read(ref _notificationCallbackThreadId);
            _handle.Dispose();
        }

        if (dispatcher is null)
        {
            dispatcherCancellation?.Dispose();
            return;
        }

        if (callbackThreadId == Environment.CurrentManagedThreadId)
        {
            _ = dispatcher.ContinueWith(
                static (_, state) => ((CancellationTokenSource?)state)?.Dispose(),
                dispatcherCancellation,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return;
        }

        try
        {
            dispatcher.GetAwaiter().GetResult();
        }
        catch
        {
            // Dispose is no-throw; delivery errors are projected through NotificationReceived.
        }
        finally
        {
            dispatcherCancellation?.Dispose();
        }
    }

    private unsafe T Query<T>(JobObjectInformationClass informationClass)
        where T : unmanaged
    {
        var value = default(T);
        uint returned = 0;
        if (NativeMethods.QueryInformationJobObject(_handle, informationClass, &value, checked((uint)sizeof(T)), &returned) == 0)
        {
            throw LastError(JobOperation.QueryInformation);
        }

        if (returned > sizeof(T))
        {
            throw new InvalidDataException($"Windows returned too much data for {informationClass}.");
        }

        return value;
    }

    private unsafe void Set<T>(JobObjectInformationClass informationClass, T value)
        where T : unmanaged
    {
        SetBuffer(informationClass, &value, checked((uint)sizeof(T)));
    }

    private unsafe void SetBuffer(JobObjectInformationClass informationClass, void* value, uint size)
    {
        if (NativeMethods.SetInformationJobObject(_handle, informationClass, value, size) == 0)
        {
            throw LastError(JobOperation.SetInformation);
        }
    }

    private static JobObjectException LastError(JobOperation operation) => new(operation, Marshal.GetLastPInvokeError());

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows job objects are only available on Windows.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
