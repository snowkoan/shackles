using System.Runtime.InteropServices;

namespace Shackles.Wfp.Internal;

internal sealed class UnmanagedMemoryScope : IDisposable
{
    private readonly List<nint> _allocations = [];
    private readonly List<nint> _localAllocations = [];

    internal nint AllocateString(string? value)
    {
        if (value is null)
        {
            return 0;
        }

        var pointer = Marshal.StringToHGlobalUni(value);
        _allocations.Add(pointer);
        return pointer;
    }

    internal nint AllocateBytes(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            return 0;
        }

        var pointer = Marshal.AllocHGlobal(value.Length);
        _allocations.Add(pointer);
        Marshal.Copy(value.ToArray(), 0, pointer, value.Length);
        return pointer;
    }

    internal nint Allocate<T>(T value)
        where T : struct
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        _allocations.Add(pointer);
        Marshal.StructureToPtr(value, pointer, fDeleteOld: false);
        return pointer;
    }

    internal nint AllocateArray<T>(IReadOnlyList<T> values)
        where T : struct
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var elementSize = Marshal.SizeOf<T>();
        var pointer = Marshal.AllocHGlobal(checked(elementSize * values.Count));
        _allocations.Add(pointer);
        for (var index = 0; index < values.Count; index++)
        {
            Marshal.StructureToPtr(
                values[index],
                pointer + checked(index * elementSize),
                fDeleteOld: false);
        }

        return pointer;
    }

    internal void TrackLocalAllocation(nint pointer)
    {
        if (pointer != 0)
        {
            _localAllocations.Add(pointer);
        }
    }

    public void Dispose()
    {
        for (var index = _localAllocations.Count - 1; index >= 0; index--)
        {
            _ = Interop.NativeMethods.LocalFree(_localAllocations[index]);
        }

        for (var index = _allocations.Count - 1; index >= 0; index--)
        {
            Marshal.FreeHGlobal(_allocations[index]);
        }

        _localAllocations.Clear();
        _allocations.Clear();
    }
}
