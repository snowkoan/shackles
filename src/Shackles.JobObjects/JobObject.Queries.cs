using System.Runtime.InteropServices;
using Shackles.JobObjects.Internal;
using Shackles.JobObjects.Interop;

namespace Shackles.JobObjects;

public sealed partial class JobObject
{
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorMoreData = 234;
    private const int MaximumGroupInformationBytes = 1024 * 1024;

    public IReadOnlyList<ProcessorGroupAffinity> GetProcessorGroups()
    {
        ThrowIfDisposed();
        var maximumGroups = NativeMethods.GetActiveProcessorGroupCount();
        var groupError = Marshal.GetLastPInvokeError();
        if (maximumGroups == 0)
        {
            throw new JobObjectException(JobOperation.QueryInformation, groupError);
        }

        var capacity = checked((int)maximumGroups);
        unsafe
        {
            while (true)
            {
                var size = checked((uint)(capacity * sizeof(NativeGroupAffinity)));
                if (size > MaximumGroupInformationBytes)
                {
                    throw new InvalidDataException("The processor-group list exceeded Shackles' safe query limit.");
                }

                var buffer = NativeMemory.AllocZeroed(size);
                try
                {
                    uint returned = 0;
                    if (NativeMethods.QueryInformationJobObject(
                            _handle,
                            JobObjectInformationClass.GroupInformationEx,
                            buffer,
                            size,
                            &returned) != 0)
                    {
                        if (returned % (uint)sizeof(NativeGroupAffinity) != 0 || returned > size)
                        {
                            throw new InvalidDataException("Windows returned malformed processor-group information.");
                        }

                        var count = checked((int)(returned / (uint)sizeof(NativeGroupAffinity)));
                        var native = (NativeGroupAffinity*)buffer;
                        var result = new ProcessorGroupAffinity[count];
                        for (var index = 0; index < count; index++)
                        {
                            result[index] = new ProcessorGroupAffinity(native[index].Group, (ulong)native[index].Mask);
                        }

                        return result;
                    }

                    var error = Marshal.GetLastPInvokeError();
                    if (error is not ErrorInsufficientBuffer and not ErrorMoreData)
                    {
                        throw new JobObjectException(JobOperation.QueryInformation, error);
                    }

                    var requiredCapacity = returned > size && returned % (uint)sizeof(NativeGroupAffinity) == 0
                        ? checked((int)(returned / (uint)sizeof(NativeGroupAffinity)))
                        : checked(capacity * 2);
                    capacity = Math.Max(capacity + 1, requiredCapacity);
                }
                finally
                {
                    NativeMemory.Free(buffer);
                }
            }
        }
    }

    public void SetProcessorGroups(IReadOnlyList<ProcessorGroupAffinity> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ThrowIfDisposed();
        Validate(groups);

        lock (_mutationGate)
        {
            if (ProcessorGroupsEqual(GetProcessorGroups(), groups))
            {
                return;
            }

            unsafe
            {
                var native = new NativeGroupAffinity[groups.Count];
                for (var index = 0; index < groups.Count; index++)
                {
                    native[index].Group = groups[index].Group;
                    native[index].Mask = JobValidation.ToNativeSize(
                        groups[index].AffinityMask,
                        $"{nameof(groups)}[{index}].{nameof(ProcessorGroupAffinity.AffinityMask)}");
                }

                fixed (NativeGroupAffinity* pointer = native)
                {
                    SetBuffer(JobObjectInformationClass.GroupInformationEx, pointer, checked((uint)(native.Length * sizeof(NativeGroupAffinity))));
                }
            }
        }
    }

    public JobAccounting GetAccounting()
    {
        ThrowIfDisposed();
        var native = Query<NativeBasicAndIoAccountingInformation>(JobObjectInformationClass.BasicAndIoAccountingInformation);
        return new JobAccounting(
            JobValidation.FromNonNegativeTicks(native.BasicInfo.TotalUserTime),
            JobValidation.FromNonNegativeTicks(native.BasicInfo.TotalKernelTime),
            JobValidation.FromNonNegativeTicks(native.BasicInfo.ThisPeriodTotalUserTime),
            JobValidation.FromNonNegativeTicks(native.BasicInfo.ThisPeriodTotalKernelTime),
            native.BasicInfo.TotalPageFaultCount,
            native.BasicInfo.TotalProcesses,
            native.BasicInfo.ActiveProcesses,
            native.BasicInfo.TotalTerminatedProcesses,
            new JobIoCounters(
                native.IoInfo.ReadOperationCount,
                native.IoInfo.WriteOperationCount,
                native.IoInfo.OtherOperationCount,
                native.IoInfo.ReadTransferCount,
                native.IoInfo.WriteTransferCount,
                native.IoInfo.OtherTransferCount));
    }

    public IReadOnlyList<int> GetProcessIds()
    {
        ThrowIfDisposed();
        const int maximumBufferBytes = 16 * 1024 * 1024;
        var capacity = 64;

        unsafe
        {
            while (true)
            {
                var size = checked((uint)(sizeof(uint) * 2 + (capacity * sizeof(nuint))));
                if (size > maximumBufferBytes)
                {
                    throw new InvalidDataException("The process list exceeded Shackles' safe query limit.");
                }

                var buffer = NativeMemory.AllocZeroed(size);
                try
                {
                    uint returned = 0;
                    if (NativeMethods.QueryInformationJobObject(
                            _handle,
                            JobObjectInformationClass.BasicProcessIdList,
                            buffer,
                            size,
                            &returned) != 0)
                    {
                        var values = (uint*)buffer;
                        var count = checked((int)values[1]);
                        if (count > capacity)
                        {
                            throw new InvalidDataException("Windows returned a process count larger than the supplied buffer.");
                        }

                        var processIds = new int[count];
                        var ids = (nuint*)((byte*)buffer + (sizeof(uint) * 2));
                        for (var index = 0; index < count; index++)
                        {
                            if (ids[index] > int.MaxValue)
                            {
                                throw new InvalidDataException("Windows returned a process ID outside the supported range.");
                            }

                            processIds[index] = (int)ids[index];
                        }

                        return processIds;
                    }

                    var error = Marshal.GetLastPInvokeError();
                    if (error is not 122 and not 234)
                    {
                        throw new JobObjectException(JobOperation.QueryInformation, error);
                    }

                    capacity = checked(capacity * 2);
                }
                finally
                {
                    NativeMemory.Free(buffer);
                }
            }
        }
    }

    private static void Validate(IReadOnlyList<ProcessorGroupAffinity> groups)
    {
        if (groups.Count == 0)
        {
            throw new ArgumentException("At least one processor-group affinity is required.", nameof(groups));
        }

        var activeGroups = NativeMethods.GetActiveProcessorGroupCount();
        var groupError = Marshal.GetLastPInvokeError();
        if (activeGroups == 0)
        {
            throw new JobObjectException(JobOperation.QueryInformation, groupError);
        }

        var seen = new HashSet<ushort>();
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            if (group is null)
            {
                throw new ArgumentException($"Processor-group entry {index} is null.", nameof(groups));
            }

            if (group.Group >= activeGroups)
            {
                throw new ArgumentOutOfRangeException(nameof(groups), group, $"Processor group {group.Group} is not active.");
            }

            if (!seen.Add(group.Group))
            {
                throw new ArgumentException($"Processor group {group.Group} appears more than once.", nameof(groups));
            }

            if (group.AffinityMask == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(groups), group, "The affinity mask must select at least one processor.");
            }

            // Processor masks can be sparse. Windows performs the authoritative active-mask check.
            _ = JobValidation.ToNativeSize(group.AffinityMask, nameof(group.AffinityMask));
        }
    }

    private static bool ProcessorGroupsEqual(
        IReadOnlyList<ProcessorGroupAffinity> first,
        IReadOnlyList<ProcessorGroupAffinity> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        foreach (var expected in second)
        {
            var found = false;
            foreach (var current in first)
            {
                if (current.Group == expected.Group && current.AffinityMask == expected.AffinityMask)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }
}
