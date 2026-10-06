namespace Shackles.App.Models;

internal sealed record NetworkBandwidthUnit(string Symbol, string Description, decimal BytesPerUnit)
{
    public static NetworkBandwidthUnit BytesPerSecond { get; } =
        new("B/s", "Bytes per second", 1m);

    public static NetworkBandwidthUnit KilobytesPerSecond { get; } =
        new("kB/s", "Kilobytes per second (1,000 bytes)", 1_000m);

    public static NetworkBandwidthUnit MegabytesPerSecond { get; } =
        new("MB/s", "Megabytes per second (1,000,000 bytes)", 1_000_000m);

    public static IReadOnlyList<NetworkBandwidthUnit> All { get; } =
        [BytesPerSecond, KilobytesPerSecond, MegabytesPerSecond];
}
