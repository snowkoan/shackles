using System.Runtime.InteropServices;
using Shackles.AppContainers.Interop;

namespace Shackles.AppContainers.Internal;

internal enum TrackedGrantKind
{
    FileSystem,
    Registry
}

internal sealed record TrackedAclGrant
{
    public required TrackedGrantKind Kind { get; init; }

    public required string Target { get; init; }

    public bool IsDirectory { get; init; }

    public FileSystemGrantAccess FileSystemAccess { get; init; }

    public RegistryGrantAccess RegistryAccess { get; init; }

    public RegistryGrantView RegistryView { get; init; }

    internal static TrackedAclGrant From(FileSystemGrant grant) => new()
    {
        Kind = TrackedGrantKind.FileSystem,
        Target = Path.GetFullPath(grant.Path),
        IsDirectory = grant.IsDirectory,
        FileSystemAccess = grant.Access
    };

    internal static TrackedAclGrant From(RegistryGrant grant) => new()
    {
        Kind = TrackedGrantKind.Registry,
        Target = RegistryPath.Normalize(grant.KeyPath),
        RegistryAccess = grant.Access,
        RegistryView = grant.View
    };

    internal string Key => Kind == TrackedGrantKind.FileSystem
        ? FormattableString.Invariant($"F|{IsDirectory}|{Target}")
        : FormattableString.Invariant($"R|{RegistryView}|{Target}");
}

internal static class AclGrantManager
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorAccessDenied = 5;
    private const byte AccessAllowedAceType = 0;
    private const byte AccessDeniedAceType = 1;
    private const byte InheritedAce = 0x10;
    private const uint ContainerInheritAce = 0x2;
    private const uint ObjectInheritAce = 0x1;

    private const uint FileReadExecute = 0x001200A9;
    private const uint FileReadWriteDelete = 0x0013019F;
    private const uint KeyRead = 0x00020019;
    private const uint KeyReadWrite = 0x0002001F;

    internal static TrackedAclGrant Normalize(FileSystemGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (string.IsNullOrWhiteSpace(grant.Path) || grant.Path.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A file-system grant requires a valid path.", nameof(grant));
        }

        var normalized = TrackedAclGrant.From(grant);
        if (normalized.IsDirectory)
        {
            if (!Directory.Exists(normalized.Target))
            {
                throw new DirectoryNotFoundException($"The granted directory does not exist: {normalized.Target}");
            }
        }
        else if (!File.Exists(normalized.Target))
        {
            throw new FileNotFoundException("The granted file does not exist.", normalized.Target);
        }

        return normalized;
    }

    internal static TrackedAclGrant Normalize(RegistryGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (string.IsNullOrWhiteSpace(grant.KeyPath) || grant.KeyPath.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A registry grant requires a valid key path.", nameof(grant));
        }

        var normalized = TrackedAclGrant.From(grant);
        using var key = RegistryPath.Open(normalized.Target, normalized.RegistryView);
        return normalized;
    }

    internal static void Apply(TrackedAclGrant grant, byte[] sidBytes)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(sidBytes);
        try
        {
            Update(grant, sidBytes, AccessMode.GrantAccess);
            if (!HasExpectedAce(grant, sidBytes, GetAccessMask(grant)))
            {
                throw new AppContainerException(
                    grant.Kind == TrackedGrantKind.FileSystem
                        ? AppContainerOperation.ApplyFileGrant
                        : AppContainerOperation.ApplyRegistryGrant,
                    $"Windows reported success but the expected sandbox access was not present on '{grant.Target}'.");
            }
        }
        catch (AppContainerException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AppContainerException(
                grant.Kind == TrackedGrantKind.FileSystem
                    ? AppContainerOperation.ApplyFileGrant
                    : AppContainerOperation.ApplyRegistryGrant,
                $"Could not grant sandbox access to '{grant.Target}'.",
                innerException: exception);
        }
    }

    internal static string? TryRevoke(TrackedAclGrant grant, byte[] sidBytes)
    {
        try
        {
            Update(grant, sidBytes, AccessMode.RevokeAccess);
            return HasExplicitAce(grant, sidBytes)
                ? $"The sandbox ACE remains on '{grant.Target}' after Windows reported successful revocation."
                : null;
        }
        catch (Exception exception)
        {
            return $"Could not revoke the sandbox grant from '{grant.Target}': {exception.Message}";
        }
    }

    private static unsafe void Update(TrackedAclGrant grant, byte[] sidBytes, AccessMode mode)
    {
        if (mode == AccessMode.RevokeAccess)
        {
            RevokeExplicitAces(grant, sidBytes);
            return;
        }

        fixed (byte* sid = sidBytes)
        {
            var entry = new NativeExplicitAccess
            {
                AccessPermissions = mode == AccessMode.GrantAccess ? GetAccessMask(grant) : 0,
                AccessMode = mode,
                Inheritance = mode == AccessMode.GrantAccess &&
                              grant.Kind == TrackedGrantKind.FileSystem &&
                              grant.IsDirectory
                    ? ContainerInheritAce | ObjectInheritAce
                    : 0,
                Trustee = new NativeTrustee
                {
                    MultipleTrusteeOperation = MultipleTrusteeOperation.NoMultipleTrustee,
                    TrusteeForm = TrusteeForm.TrusteeIsSid,
                    TrusteeType = TrusteeType.TrusteeIsGroup,
                    Name = (nint)sid
                }
            };

            if (grant.Kind == TrackedGrantKind.FileSystem)
            {
                UpdateFileSystemAcl(grant.Target, &entry);
            }
            else
            {
                using var key = RegistryPath.Open(grant.Target, grant.RegistryView);
                UpdateRegistryAcl(key, &entry);
            }
        }
    }

    private static unsafe void RevokeExplicitAces(
        TrackedAclGrant grant,
        byte[] sidBytes)
    {
        fixed (byte* sid = sidBytes)
        {
            if (grant.Kind == TrackedGrantKind.FileSystem)
            {
                RebuildFileSystemAclWithoutSid(grant.Target, (nint)sid);
            }
            else
            {
                using var key = RegistryPath.Open(
                    grant.Target,
                    grant.RegistryView);
                RebuildRegistryAclWithoutSid(key, grant.Target, (nint)sid);
            }
        }
    }

    private static void RebuildFileSystemAclWithoutSid(
        string path,
        nint sid)
    {
        var result = NativeMethods.GetNamedSecurityInfo(
            path,
            SecurityObjectType.FileObject,
            SecurityInformation.Dacl,
            out _,
            out _,
            out var oldAcl,
            out _,
            out var securityDescriptor);
        if (result != ErrorSuccess)
        {
            throw AppContainerException.FromWin32(
                AppContainerOperation.RevokeGrant,
                checked((int)result),
                $"Could not read the ACL on '{path}' for cleanup.");
        }

        nint newAcl = 0;
        try
        {
            newAcl = BuildAclWithoutExplicitSid(oldAcl, sid);
            if (newAcl == 0)
            {
                return;
            }

            result = NativeMethods.SetNamedSecurityInfo(
                path,
                SecurityObjectType.FileObject,
                SecurityInformation.Dacl,
                0,
                0,
                newAcl,
                0);
            if (result != ErrorSuccess)
            {
                throw AppContainerException.FromWin32(
                    AppContainerOperation.RevokeGrant,
                    checked((int)result),
                    $"Could not write the cleaned ACL on '{path}'.");
            }
        }
        finally
        {
            if (newAcl != 0)
            {
                Marshal.FreeHGlobal(newAcl);
            }

            if (securityDescriptor != 0)
            {
                _ = NativeMethods.LocalFree(securityDescriptor);
            }
        }
    }

    private static void RebuildRegistryAclWithoutSid(
        SafeRegistryKeyHandle key,
        string path,
        nint sid)
    {
        var result = NativeMethods.GetSecurityInfo(
            key,
            SecurityObjectType.RegistryKey,
            SecurityInformation.Dacl,
            out _,
            out _,
            out var oldAcl,
            out _,
            out var securityDescriptor);
        if (result != ErrorSuccess)
        {
            throw AppContainerException.FromWin32(
                AppContainerOperation.RevokeGrant,
                checked((int)result),
                $"Could not read registry ACL '{path}' for cleanup.");
        }

        nint newAcl = 0;
        try
        {
            newAcl = BuildAclWithoutExplicitSid(oldAcl, sid);
            if (newAcl == 0)
            {
                return;
            }

            result = NativeMethods.SetSecurityInfo(
                key,
                SecurityObjectType.RegistryKey,
                SecurityInformation.Dacl,
                0,
                0,
                newAcl,
                0);
            if (result != ErrorSuccess)
            {
                throw AppContainerException.FromWin32(
                    AppContainerOperation.RevokeGrant,
                    checked((int)result),
                    $"Could not write cleaned registry ACL '{path}'.");
            }
        }
        finally
        {
            if (newAcl != 0)
            {
                Marshal.FreeHGlobal(newAcl);
            }

            if (securityDescriptor != 0)
            {
                _ = NativeMethods.LocalFree(securityDescriptor);
            }
        }
    }

    private static nint BuildAclWithoutExplicitSid(nint oldAcl, nint sid)
    {
        if (oldAcl == 0)
        {
            return 0;
        }

        var aclHeader = Marshal.PtrToStructure<NativeAclHeader>(oldAcl);
        var keptAces = new List<(nint Pointer, uint Size)>();
        var removed = false;
        var newAclSize = checked((uint)Marshal.SizeOf<NativeAclHeader>());
        for (var index = 0u; index < aclHeader.AceCount; index++)
        {
            if (NativeMethods.GetAce(oldAcl, index, out var ace) == 0 ||
                ace == 0)
            {
                throw AppContainerException.FromWin32(
                    AppContainerOperation.RevokeGrant,
                    Marshal.GetLastPInvokeError(),
                    "Could not enumerate an ACL during sandbox cleanup.");
            }

            var aceHeader = Marshal.PtrToStructure<NativeAceHeader>(ace);
            var isOurExplicitAce =
                (aceHeader.Flags & InheritedAce) == 0 &&
                aceHeader.Type is AccessAllowedAceType or AccessDeniedAceType &&
                NativeMethods.EqualSid(ace + 8, sid) != 0;
            if (isOurExplicitAce)
            {
                removed = true;
                continue;
            }

            var size = (uint)aceHeader.Size;
            keptAces.Add((ace, size));
            newAclSize = checked(newAclSize + size);
        }

        if (!removed)
        {
            return 0;
        }

        var newAcl = Marshal.AllocHGlobal(checked((int)newAclSize));
        if (NativeMethods.InitializeAcl(
                newAcl,
                newAclSize,
                aclHeader.Revision) == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            Marshal.FreeHGlobal(newAcl);
            throw AppContainerException.FromWin32(
                AppContainerOperation.RevokeGrant,
                error,
                "Could not initialize a clean replacement ACL.");
        }

        foreach (var ace in keptAces)
        {
            if (NativeMethods.AddAce(
                    newAcl,
                    aclHeader.Revision,
                    uint.MaxValue,
                    ace.Pointer,
                    ace.Size) == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                Marshal.FreeHGlobal(newAcl);
                throw AppContainerException.FromWin32(
                    AppContainerOperation.RevokeGrant,
                    error,
                    "Could not preserve an existing ACE during sandbox cleanup.");
            }
        }

        return newAcl;
    }

    private static unsafe void UpdateFileSystemAcl(string path, NativeExplicitAccess* entry)
    {
        var result = NativeMethods.GetNamedSecurityInfo(
            path,
            SecurityObjectType.FileObject,
            SecurityInformation.Dacl,
            out _,
            out _,
            out var oldAcl,
            out _,
            out var securityDescriptor);
        if (result != ErrorSuccess)
        {
            throw AppContainerException.FromWin32(
                AppContainerOperation.ApplyFileGrant,
                checked((int)result),
                $"Could not read the ACL on '{path}'.");
        }

        nint newAcl = 0;
        try
        {
            result = NativeMethods.SetEntriesInAcl(1, entry, oldAcl, out newAcl);
            if (result != ErrorSuccess)
            {
                throw AppContainerException.FromWin32(
                    AppContainerOperation.ApplyFileGrant,
                    checked((int)result),
                    $"Could not prepare the ACL update for '{path}'.");
            }

            result = NativeMethods.SetNamedSecurityInfo(
                path,
                SecurityObjectType.FileObject,
                SecurityInformation.Dacl,
                0,
                0,
                newAcl,
                0);
            if (result != ErrorSuccess)
            {
                var permissionHint = result == ErrorAccessDenied
                    ? " Shackles needs permission to change this object's ACL (WRITE_DAC); permission to modify its contents is not sufficient."
                    : string.Empty;
                throw AppContainerException.FromWin32(
                    AppContainerOperation.ApplyFileGrant,
                    checked((int)result),
                    $"Could not write the ACL on '{path}'." + permissionHint);
            }
        }
        finally
        {
            if (newAcl != 0)
            {
                _ = NativeMethods.LocalFree(newAcl);
            }

            if (securityDescriptor != 0)
            {
                _ = NativeMethods.LocalFree(securityDescriptor);
            }
        }
    }

    private static unsafe void UpdateRegistryAcl(SafeRegistryKeyHandle key, NativeExplicitAccess* entry)
    {
        var result = NativeMethods.GetSecurityInfo(
            key,
            SecurityObjectType.RegistryKey,
            SecurityInformation.Dacl,
            out _,
            out _,
            out var oldAcl,
            out _,
            out var securityDescriptor);
        if (result != ErrorSuccess)
        {
            throw AppContainerException.FromWin32(
                AppContainerOperation.ApplyRegistryGrant,
                checked((int)result),
                "Could not read the registry key ACL.");
        }

        nint newAcl = 0;
        try
        {
            result = NativeMethods.SetEntriesInAcl(1, entry, oldAcl, out newAcl);
            if (result != ErrorSuccess)
            {
                throw AppContainerException.FromWin32(
                    AppContainerOperation.ApplyRegistryGrant,
                    checked((int)result),
                    "Could not prepare the registry ACL update.");
            }

            result = NativeMethods.SetSecurityInfo(
                key,
                SecurityObjectType.RegistryKey,
                SecurityInformation.Dacl,
                0,
                0,
                newAcl,
                0);
            if (result != ErrorSuccess)
            {
                var permissionHint = result == ErrorAccessDenied
                    ? " Shackles needs permission to change this registry key's ACL (WRITE_DAC)."
                    : string.Empty;
                throw AppContainerException.FromWin32(
                    AppContainerOperation.ApplyRegistryGrant,
                    checked((int)result),
                    "Could not write the registry key ACL." + permissionHint);
            }
        }
        finally
        {
            if (newAcl != 0)
            {
                _ = NativeMethods.LocalFree(newAcl);
            }

            if (securityDescriptor != 0)
            {
                _ = NativeMethods.LocalFree(securityDescriptor);
            }
        }
    }

    private static bool HasExpectedAce(TrackedAclGrant grant, byte[] sidBytes, uint expectedMask) =>
        InspectAcl(grant, sidBytes, expectedMask, requireMask: true);

    private static bool HasExplicitAce(TrackedAclGrant grant, byte[] sidBytes) =>
        InspectAcl(grant, sidBytes, expectedMask: 0, requireMask: false);

    private static unsafe bool InspectAcl(
        TrackedAclGrant grant,
        byte[] sidBytes,
        uint expectedMask,
        bool requireMask)
    {
        nint securityDescriptor = 0;
        nint acl;
        SafeRegistryKeyHandle? key = null;
        try
        {
            uint result;
            if (grant.Kind == TrackedGrantKind.FileSystem)
            {
                result = NativeMethods.GetNamedSecurityInfo(
                    grant.Target,
                    SecurityObjectType.FileObject,
                    SecurityInformation.Dacl,
                    out _,
                    out _,
                    out acl,
                    out _,
                    out securityDescriptor);
            }
            else
            {
                key = RegistryPath.Open(grant.Target, grant.RegistryView);
                result = NativeMethods.GetSecurityInfo(
                    key,
                    SecurityObjectType.RegistryKey,
                    SecurityInformation.Dacl,
                    out _,
                    out _,
                    out acl,
                    out _,
                    out securityDescriptor);
            }

            if (result != ErrorSuccess)
            {
                throw AppContainerException.FromWin32(
                    AppContainerOperation.RevokeGrant,
                    checked((int)result),
                    $"Could not verify the ACL on '{grant.Target}'.");
            }

            if (acl == 0)
            {
                return false;
            }

            var header = Marshal.PtrToStructure<NativeAclHeader>(acl);
            uint combinedMask = 0;
            fixed (byte* sid = sidBytes)
            {
                for (var index = 0u; index < header.AceCount; index++)
                {
                    if (NativeMethods.GetAce(acl, index, out var ace) == 0 || ace == 0)
                    {
                        continue;
                    }

                    var aceHeader = Marshal.PtrToStructure<NativeAceHeader>(ace);
                    if ((aceHeader.Flags & InheritedAce) != 0 ||
                        aceHeader.Type is not (AccessAllowedAceType or AccessDeniedAceType))
                    {
                        continue;
                    }

                    var aceSid = ace + 8;
                    if (NativeMethods.EqualSid((nint)sid, aceSid) == 0)
                    {
                        continue;
                    }

                    if (!requireMask)
                    {
                        return true;
                    }

                    if (aceHeader.Type == AccessAllowedAceType)
                    {
                        combinedMask |= unchecked((uint)Marshal.ReadInt32(ace, 4));
                    }
                }
            }

            return requireMask &&
                   (combinedMask & expectedMask) == expectedMask;
        }
        finally
        {
            key?.Dispose();
            if (securityDescriptor != 0)
            {
                _ = NativeMethods.LocalFree(securityDescriptor);
            }
        }
    }

    private static uint GetAccessMask(TrackedAclGrant grant) => grant.Kind switch
    {
        TrackedGrantKind.FileSystem when grant.FileSystemAccess == FileSystemGrantAccess.ReadExecute => FileReadExecute,
        TrackedGrantKind.FileSystem => FileReadWriteDelete,
        TrackedGrantKind.Registry when grant.RegistryAccess == RegistryGrantAccess.Read => KeyRead,
        _ => KeyReadWrite
    };
}

internal static class RegistryPath
{
    private const uint ReadControl = 0x00020000;
    private const uint WriteDac = 0x00040000;
    private const uint KeyWow6432 = 0x00000200;
    private const uint KeyWow6464 = 0x00000100;

    internal static string Normalize(string keyPath)
    {
        var trimmed = keyPath.Trim().TrimEnd('\\');
        var separator = trimmed.IndexOf('\\');
        var rootName = separator < 0 ? trimmed : trimmed[..separator];
        var subKey = separator < 0 ? string.Empty : trimmed[(separator + 1)..].TrimStart('\\');
        var canonicalRoot = ParseRoot(rootName).CanonicalName;
        return subKey.Length == 0 ? canonicalRoot : $"{canonicalRoot}\\{subKey}";
    }

    internal static SafeRegistryKeyHandle Open(string normalizedPath, RegistryGrantView view)
    {
        var separator = normalizedPath.IndexOf('\\');
        var rootName = separator < 0 ? normalizedPath : normalizedPath[..separator];
        var subKey = separator < 0 ? string.Empty : normalizedPath[(separator + 1)..];
        var root = ParseRoot(rootName);
        var viewAccess = view switch
        {
            RegistryGrantView.Registry32 => KeyWow6432,
            RegistryGrantView.Registry64 => KeyWow6464,
            _ => 0u
        };

        var result = NativeMethods.RegOpenKeyEx(
            root.Handle,
            subKey,
            0,
            ReadControl | WriteDac | viewAccess,
            out var rawKey);
        var key = new SafeRegistryKeyHandle(rawKey);
        if (result != 0)
        {
            key.Dispose();
            throw AppContainerException.FromWin32(
                AppContainerOperation.ApplyRegistryGrant,
                result,
                $"Could not open registry key '{normalizedPath}' for ACL management.");
        }

        return key;
    }

    private static (nint Handle, string CanonicalName) ParseRoot(string rootName) =>
        rootName.ToUpperInvariant() switch
        {
            "HKCR" or "HKEY_CLASSES_ROOT" => (unchecked((nint)0x80000000u), "HKEY_CLASSES_ROOT"),
            "HKCU" or "HKEY_CURRENT_USER" => (unchecked((nint)0x80000001u), "HKEY_CURRENT_USER"),
            "HKLM" or "HKEY_LOCAL_MACHINE" => (unchecked((nint)0x80000002u), "HKEY_LOCAL_MACHINE"),
            "HKU" or "HKEY_USERS" => (unchecked((nint)0x80000003u), "HKEY_USERS"),
            "HKCC" or "HKEY_CURRENT_CONFIG" => (unchecked((nint)0x80000005u), "HKEY_CURRENT_CONFIG"),
            _ => throw new ArgumentException($"Unsupported registry root '{rootName}'. Use HKCU, HKLM, HKCR, HKU, or HKCC.")
        };
}
