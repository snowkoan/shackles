using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Shackles.Wfp.Internal;

internal sealed record NormalizedWfpRule(
    string ExecutablePath,
    WfpTrafficDirection Directions,
    WfpIpVersion IpVersions,
    WfpTransportProtocol Protocol,
    ushort? LocalPort,
    ushort? RemotePort,
    ParsedNetwork? LocalNetwork,
    ParsedNetwork? RemoteNetwork,
    ulong? InterfaceLuid,
    string? InterfaceName,
    bool CurrentUserOnly)
{
    internal WfpBlockRuleOptions ToOptions() => new(
        ExecutablePath,
        Directions,
        IpVersions,
        Protocol,
        LocalPort,
        RemotePort,
        LocalNetwork?.CanonicalText,
        RemoteNetwork?.CanonicalText,
        InterfaceLuid,
        InterfaceName,
        CurrentUserOnly);
}

internal sealed record ParsedNetwork(
    AddressFamily AddressFamily,
    byte[] NetworkBytes,
    byte PrefixLength,
    string CanonicalText)
{
    internal uint GetIpv4AddressHostOrder() =>
        BinaryPrimitives.ReadUInt32BigEndian(NetworkBytes);

    internal uint GetIpv4MaskHostOrder() => PrefixLength == 0
        ? 0
        : uint.MaxValue << (32 - PrefixLength);

    internal ParsedNetwork ToIpv4MappedIpv6()
    {
        if (AddressFamily != AddressFamily.InterNetwork || NetworkBytes.Length != 4)
        {
            throw new InvalidOperationException(
                "Only an IPv4 network can be converted to an IPv4-mapped IPv6 network.");
        }

        var mappedBytes = new byte[16];
        mappedBytes[10] = 0xFF;
        mappedBytes[11] = 0xFF;
        NetworkBytes.CopyTo(mappedBytes, 12);
        var mappedPrefixLength = checked((byte)(96 + PrefixLength));
        return new ParsedNetwork(
            AddressFamily.InterNetworkV6,
            mappedBytes,
            mappedPrefixLength,
            $"{new IPAddress(mappedBytes)}/{mappedPrefixLength}");
    }
}

internal static class WfpRuleNormalizer
{
    internal static NormalizedWfpRule Normalize(
        WfpBlockRuleOptions options,
        bool requireExistingExecutable = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            throw new ArgumentException("Choose an executable before adding a WFP rule.", nameof(options));
        }

        var executablePath = options.ExecutablePath.Trim();
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException(
                "WFP application identity requires a fully qualified executable path.",
                nameof(options));
        }

        executablePath = Path.GetFullPath(executablePath);
        if (requireExistingExecutable && !File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "The executable must exist so Windows can derive its WFP application identity.",
                executablePath);
        }

        if (options.Directions == WfpTrafficDirection.None ||
            (options.Directions & ~WfpTrafficDirection.Both) != 0)
        {
            throw new ArgumentException("Select inbound, outbound, or both directions.", nameof(options));
        }

        if (options.IpVersions == WfpIpVersion.None ||
            (options.IpVersions & ~WfpIpVersion.All) != 0)
        {
            throw new ArgumentException("Select IPv4, IPv6, or both IP versions.", nameof(options));
        }

        if (!Enum.IsDefined(options.Protocol))
        {
            throw new ArgumentException("Choose a supported transport protocol.", nameof(options));
        }

        if ((options.LocalPort.HasValue || options.RemotePort.HasValue) &&
            options.Protocol is not (WfpTransportProtocol.Tcp or WfpTransportProtocol.Udp))
        {
            throw new ArgumentException(
                "Local and remote ports can be used only with TCP or UDP.",
                nameof(options));
        }

        if (options.InterfaceLuid == 0)
        {
            throw new ArgumentException("The selected interface has an invalid LUID.", nameof(options));
        }

        var interfaceName = string.IsNullOrWhiteSpace(options.InterfaceName)
            ? null
            : options.InterfaceName.Trim();
        if (interfaceName is not null && options.InterfaceLuid is null)
        {
            throw new ArgumentException(
                "An interface name is descriptive only; select its interface LUID as well.",
                nameof(options));
        }

        var localNetwork = ParseNetwork(options.LocalNetwork, "local");
        var remoteNetwork = ParseNetwork(options.RemoteNetwork, "remote");
        if (localNetwork is not null && remoteNetwork is not null &&
            localNetwork.AddressFamily != remoteNetwork.AddressFamily)
        {
            throw new ArgumentException(
                "Local and remote network scopes must use the same IP version.",
                nameof(options));
        }

        var scopedFamily = localNetwork?.AddressFamily ?? remoteNetwork?.AddressFamily;
        var effectiveVersions = scopedFamily switch
        {
            AddressFamily.InterNetwork => WfpIpVersion.Ipv4,
            AddressFamily.InterNetworkV6 => WfpIpVersion.Ipv6,
            _ => options.IpVersions
        };
        if ((options.IpVersions & effectiveVersions) == 0)
        {
            throw new ArgumentException(
                $"The configured network scope is {Describe(effectiveVersions)}, but that IP version is not selected.",
                nameof(options));
        }

        return new NormalizedWfpRule(
            executablePath,
            options.Directions,
            effectiveVersions,
            options.Protocol,
            options.LocalPort,
            options.RemotePort,
            localNetwork,
            remoteNetwork,
            options.InterfaceLuid,
            interfaceName,
            options.CurrentUserOnly);
    }

    internal static ParsedNetwork? ParseNetwork(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        var slash = text.IndexOf('/');
        if (slash != text.LastIndexOf('/'))
        {
            throw new ArgumentException($"The {label} network must be an IP address or CIDR prefix.");
        }

        var addressText = slash < 0 ? text : text[..slash];
        if (addressText.Contains('%'))
        {
            throw new ArgumentException(
                $"The {label} IPv6 network cannot contain a zone index; choose an interface separately.");
        }

        if (!IPAddress.TryParse(addressText, out var address) ||
            address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            throw new ArgumentException($"'{value}' is not a valid {label} IPv4 or IPv6 network.");
        }

        var bitCount = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefixLength = bitCount;
        if (slash >= 0 &&
            (!int.TryParse(text[(slash + 1)..], out prefixLength) ||
             prefixLength < 0 ||
             prefixLength > bitCount))
        {
            throw new ArgumentException(
                $"The {label} network prefix length must be between 0 and {bitCount}.");
        }

        var bytes = address.GetAddressBytes();
        ApplyPrefix(bytes, prefixLength);
        if (address.AddressFamily == AddressFamily.InterNetworkV6 &&
            OverlapsIpv4MappedRange(bytes, prefixLength))
        {
            throw new ArgumentException(
                $"The {label} IPv6 network overlaps the IPv4-mapped range. " +
                "Use an IPv4 scope for mapped traffic, or leave the address empty to match all native IPv6 addresses.");
        }

        var networkAddress = new IPAddress(bytes);
        return new ParsedNetwork(
            address.AddressFamily,
            bytes,
            checked((byte)prefixLength),
            $"{networkAddress}/{prefixLength}");
    }

    private static void ApplyPrefix(byte[] bytes, int prefixLength)
    {
        var wholeBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        if (remainingBits != 0)
        {
            bytes[wholeBytes] &= unchecked((byte)(0xFF << (8 - remainingBits)));
            wholeBytes++;
        }

        Array.Clear(bytes, wholeBytes, bytes.Length - wholeBytes);
    }

    private static bool OverlapsIpv4MappedRange(byte[] networkBytes, int prefixLength)
    {
        ReadOnlySpan<byte> mappedPrefix =
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF];
        var comparedBits = Math.Min(prefixLength, 96);
        var wholeBytes = comparedBits / 8;
        if (!networkBytes.AsSpan(0, wholeBytes).SequenceEqual(mappedPrefix[..wholeBytes]))
        {
            return false;
        }

        var remainingBits = comparedBits % 8;
        if (remainingBits == 0)
        {
            return true;
        }

        var mask = unchecked((byte)(0xFF << (8 - remainingBits)));
        return (networkBytes[wholeBytes] & mask) == (mappedPrefix[wholeBytes] & mask);
    }

    private static string Describe(WfpIpVersion version) => version switch
    {
        WfpIpVersion.Ipv4 => "IPv4",
        WfpIpVersion.Ipv6 => "IPv6",
        _ => version.ToString()
    };
}
