using System.Runtime.InteropServices;

namespace Shackles.AppContainers.Interop;

[Flags]
internal enum ProcessCreationFlags : uint
{
    UnicodeEnvironment = 0x00000400,
    ExtendedStartupInfoPresent = 0x00080000
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeStartupInfo
{
    internal uint Size;
    internal nint Reserved;
    internal nint Desktop;
    internal nint Title;
    internal uint X;
    internal uint Y;
    internal uint XSize;
    internal uint YSize;
    internal uint XCountChars;
    internal uint YCountChars;
    internal uint FillAttribute;
    internal uint Flags;
    internal ushort ShowWindow;
    internal ushort Reserved2Size;
    internal nint Reserved2;
    internal nint StandardInput;
    internal nint StandardOutput;
    internal nint StandardError;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeStartupInfoEx
{
    internal NativeStartupInfo StartupInfo;
    internal nint AttributeList;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeProcessInformation
{
    internal nint Process;
    internal nint Thread;
    internal uint ProcessId;
    internal uint ThreadId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSecurityCapabilities
{
    internal nint AppContainerSid;
    internal nint Capabilities;
    internal uint CapabilityCount;
    internal uint Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSidAndAttributes
{
    internal nint Sid;
    internal uint Attributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFileTime
{
    internal uint LowDateTime;
    internal uint HighDateTime;

    internal long ToLong() => unchecked((long)(((ulong)HighDateTime << 32) | LowDateTime));
}

internal enum SecurityObjectType
{
    FileObject = 1,
    RegistryKey = 4
}

[Flags]
internal enum SecurityInformation : uint
{
    Dacl = 0x00000004
}

internal enum AccessMode
{
    NotUsedAccess,
    GrantAccess,
    SetAccess,
    DenyAccess,
    RevokeAccess,
    SetAuditSuccess,
    SetAuditFailure
}

internal enum MultipleTrusteeOperation
{
    NoMultipleTrustee,
    TrusteeIsImpersonate
}

internal enum TrusteeForm
{
    TrusteeIsSid,
    TrusteeIsName,
    TrusteeBadForm,
    TrusteeIsObjectsAndSid,
    TrusteeIsObjectsAndName
}

internal enum TrusteeType
{
    TrusteeIsUnknown,
    TrusteeIsUser,
    TrusteeIsGroup,
    TrusteeIsDomain,
    TrusteeIsAlias,
    TrusteeIsWellKnownGroup,
    TrusteeIsDeleted,
    TrusteeIsInvalid,
    TrusteeIsComputer
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTrustee
{
    internal nint MultipleTrustee;
    internal MultipleTrusteeOperation MultipleTrusteeOperation;
    internal TrusteeForm TrusteeForm;
    internal TrusteeType TrusteeType;
    internal nint Name;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeExplicitAccess
{
    internal uint AccessPermissions;
    internal AccessMode AccessMode;
    internal uint Inheritance;
    internal NativeTrustee Trustee;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeAclHeader
{
    internal byte Revision;
    internal byte Reserved1;
    internal ushort Size;
    internal ushort AceCount;
    internal ushort Reserved2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeAceHeader
{
    internal byte Type;
    internal byte Flags;
    internal ushort Size;
}
