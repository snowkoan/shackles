using System.Runtime.InteropServices;

namespace Shackles.Wfp.Interop;

internal enum FwpDataType : uint
{
    Empty = 0,
    UInt8 = 1,
    UInt16 = 2,
    UInt32 = 3,
    UInt64 = 4,
    ByteArray16 = 11,
    ByteBlob = 12,
    SecurityDescriptor = 14,
    V4AddressMask = 0x100,
    V6AddressMask = 0x101
}

internal enum FwpMatchType : uint
{
    Equal = 0,
    Greater = 1,
    Less = 2
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeDisplayData
{
    internal nint Name;
    internal nint Description;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeByteBlob
{
    internal uint Size;
    internal nint Data;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeByteArray16
{
    internal fixed byte Value[16];

    internal static NativeByteArray16 Create(ReadOnlySpan<byte> value)
    {
        if (value.Length != 16)
        {
            throw new ArgumentException("An IPv6 address must contain 16 bytes.", nameof(value));
        }

        var result = new NativeByteArray16();
        for (var index = 0; index < value.Length; index++)
        {
            result.Value[index] = value[index];
        }

        return result;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeValue
{
    internal FwpDataType Type;
    internal nint Value;

    internal static NativeValue Empty => new() { Type = FwpDataType.Empty };
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeConditionValue
{
    internal FwpDataType Type;
    internal nint Value;

    internal static NativeConditionValue UInt8(byte value) => new()
    {
        Type = FwpDataType.UInt8,
        Value = value
    };

    internal static NativeConditionValue UInt16(ushort value) => new()
    {
        Type = FwpDataType.UInt16,
        Value = value
    };

    internal static NativeConditionValue Pointer(FwpDataType type, nint pointer) => new()
    {
        Type = type,
        Value = pointer
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeV4AddressAndMask
{
    internal uint Address;
    internal uint Mask;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeV6AddressAndMask
{
    internal fixed byte Address[16];
    internal byte PrefixLength;

    internal static NativeV6AddressAndMask Create(ReadOnlySpan<byte> address, byte prefixLength)
    {
        if (address.Length != 16)
        {
            throw new ArgumentException("An IPv6 address must contain 16 bytes.", nameof(address));
        }

        var result = new NativeV6AddressAndMask { PrefixLength = prefixLength };
        for (var index = 0; index < address.Length; index++)
        {
            result.Address[index] = address[index];
        }

        return result;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSession
{
    internal Guid SessionKey;
    internal NativeDisplayData DisplayData;
    internal uint Flags;
    internal uint TransactionWaitTimeoutMilliseconds;
    internal uint ProcessId;
    internal nint Sid;
    internal nint UserName;
    internal int KernelMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeProvider
{
    internal Guid ProviderKey;
    internal NativeDisplayData DisplayData;
    internal uint Flags;
    internal NativeByteBlob ProviderData;
    internal nint ServiceName;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSubLayer
{
    internal Guid SubLayerKey;
    internal NativeDisplayData DisplayData;
    internal uint Flags;
    internal nint ProviderKey;
    internal NativeByteBlob ProviderData;
    internal ushort Weight;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFilterCondition
{
    internal Guid FieldKey;
    internal FwpMatchType MatchType;
    internal NativeConditionValue ConditionValue;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeAction
{
    internal uint Type;
    internal Guid ActionKey;
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct NativeFilterContext
{
    [FieldOffset(0)]
    internal ulong RawContext;

    [FieldOffset(0)]
    internal Guid ProviderContextKey;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFilter
{
    internal Guid FilterKey;
    internal NativeDisplayData DisplayData;
    internal uint Flags;
    internal nint ProviderKey;
    internal NativeByteBlob ProviderData;
    internal Guid LayerKey;
    internal Guid SubLayerKey;
    internal NativeValue Weight;
    internal uint ConditionCount;
    internal nint Conditions;
    internal NativeAction Action;
    internal NativeFilterContext Context;
    internal nint Reserved;
    internal ulong FilterId;
    internal NativeValue EffectiveWeight;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTokenElevation
{
    internal int TokenIsElevated;
}
