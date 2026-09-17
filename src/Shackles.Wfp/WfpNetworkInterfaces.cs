using System.Net.NetworkInformation;
using Shackles.Wfp.Interop;

namespace Shackles.Wfp;

public static class WfpNetworkInterfaces
{
    public static IReadOnlyList<WfpNetworkInterface> GetAll()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var interfaces = new List<WfpNetworkInterface>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!Guid.TryParse(adapter.Id, out var interfaceGuid))
            {
                continue;
            }

            var error = NativeMethods.ConvertInterfaceGuidToLuid(
                in interfaceGuid,
                out var luid);
            if (error != 0 || luid == 0)
            {
                continue;
            }

            uint? ipv4Index = null;
            uint? ipv6Index = null;
            try
            {
                var properties = adapter.GetIPProperties();
                var ipv4 = properties.GetIPv4Properties();
                var ipv6 = properties.GetIPv6Properties();
                ipv4Index = ipv4 is null ? null : checked((uint)ipv4.Index);
                ipv6Index = ipv6 is null ? null : checked((uint)ipv6.Index);
            }
            catch (NetworkInformationException)
            {
                // The stable LUID remains useful even when optional index
                // metadata cannot be queried for a transient adapter.
            }

            interfaces.Add(new WfpNetworkInterface(
                luid,
                adapter.Name,
                adapter.Description,
                adapter.NetworkInterfaceType.ToString(),
                adapter.OperationalStatus.ToString(),
                ipv4Index,
                ipv6Index));
        }

        return interfaces
            .DistinctBy(item => item.Luid)
            .OrderByDescending(item => string.Equals(item.Status, "Up", StringComparison.Ordinal))
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
