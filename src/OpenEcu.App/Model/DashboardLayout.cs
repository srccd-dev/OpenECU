namespace OpenEcu.App.Model;

/// <summary>
/// Which PIDs appear as hero gauges vs. tiles. Data-driven so v2 can let users edit + persist
/// it without touching the views.
/// </summary>
public sealed record DashboardLayout(IReadOnlyList<byte> HeroPids, IReadOnlyList<byte> TilePids)
{
    public static DashboardLayout Default { get; } = new(
        HeroPids: new byte[] { 0x0C, 0x05 },                               // RPM, coolant
        TilePids: new byte[] { 0x11, 0x0F, 0x04, 0x0E, 0x14, 0x0D });      // throttle, intake, load, timing, O2, speed
}
