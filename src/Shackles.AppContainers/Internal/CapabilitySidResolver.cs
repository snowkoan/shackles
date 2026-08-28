using System.Runtime.InteropServices;
using System.Security.Principal;
using Shackles.AppContainers.Interop;

namespace Shackles.AppContainers.Internal;

internal static class CapabilitySidResolver
{
    internal static IReadOnlyList<byte[]> Resolve(IReadOnlyCollection<string> capabilityNames)
    {
        ArgumentNullException.ThrowIfNull(capabilityNames);
        var result = new List<byte[]>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capabilityName in capabilityNames
                     .Select(item => item.Trim())
                     .Where(item => item.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var sidBytes in ResolveOne(capabilityName))
            {
                var sid = new SecurityIdentifier(sidBytes, 0);
                if (seen.Add(sid.Value))
                {
                    result.Add(sidBytes);
                }
            }
        }

        return result;
    }

    private static List<byte[]> ResolveOne(string capabilityName)
    {
        if (capabilityName.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A capability name cannot contain a null character.", nameof(capabilityName));
        }

        if (NativeMethods.DeriveCapabilitySidsFromName(
                capabilityName,
                out var groupSids,
                out var groupSidCount,
                out var capabilitySids,
                out var capabilitySidCount) == 0)
        {
            throw AppContainerException.FromWin32(
                AppContainerOperation.DeriveCapability,
                Marshal.GetLastPInvokeError(),
                $"Windows could not resolve capability '{capabilityName}'.");
        }

        var result = new List<byte[]>();
        try
        {
            FreeSidArray(groupSids, groupSidCount, output: null);
            groupSids = 0;
            if (capabilitySidCount == 0)
            {
                throw new AppContainerException(
                    AppContainerOperation.DeriveCapability,
                    $"Windows returned no capability SID for '{capabilityName}'.");
            }

            FreeSidArray(capabilitySids, capabilitySidCount, result);
            capabilitySids = 0;
            return result;
        }
        finally
        {
            if (groupSids != 0)
            {
                FreeSidArray(groupSids, groupSidCount, output: null);
            }

            if (capabilitySids != 0)
            {
                FreeSidArray(capabilitySids, capabilitySidCount, output: null);
            }
        }
    }

    private static void FreeSidArray(nint array, uint count, List<byte[]>? output)
    {
        if (array == 0)
        {
            return;
        }

        for (var index = 0u; index < count; index++)
        {
            var sidPointer = Marshal.ReadIntPtr(array, checked((int)(index * (uint)nint.Size)));
            if (sidPointer == 0)
            {
                continue;
            }

            try
            {
                if (output is not null)
                {
                    var sid = new SecurityIdentifier(sidPointer);
                    var sidBytes = new byte[sid.BinaryLength];
                    sid.GetBinaryForm(sidBytes, 0);
                    output.Add(sidBytes);
                }
            }
            finally
            {
                _ = NativeMethods.LocalFree(sidPointer);
            }
        }

        _ = NativeMethods.LocalFree(array);
    }
}
