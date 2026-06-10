namespace OpenEcu.App.Model;

/// <summary>Display metadata for one OBD metric: how to label, scale, and color it.</summary>
public sealed record MetricDescriptor(byte Pid, string Name, string Unit, double Min, double Max, string Accent);

/// <summary>Catalog of known PIDs with gauge metadata; safe fallback for unknown PIDs.</summary>
public static class MetricCatalog
{
    private static readonly IReadOnlyDictionary<byte, MetricDescriptor> Map = new Dictionary<byte, MetricDescriptor>
    {
        [0x04] = new(0x04, "Engine load", "%", 0, 100, "teal"),
        [0x05] = new(0x05, "Coolant temperature", "°C", -40, 150, "teal"),
        [0x06] = new(0x06, "Short-term fuel trim", "%", -100, 100, "teal"),
        [0x07] = new(0x07, "Long-term fuel trim", "%", -100, 100, "teal"),
        [0x0B] = new(0x0B, "Intake manifold pressure", "kPa", 0, 255, "teal"),
        [0x0C] = new(0x0C, "Engine RPM", "rpm", 0, 12000, "blue"),
        [0x0D] = new(0x0D, "Vehicle speed", "km/h", 0, 300, "blue"),
        [0x0E] = new(0x0E, "Timing advance", "°", -64, 64, "teal"),
        [0x0F] = new(0x0F, "Intake air temperature", "°C", -40, 150, "teal"),
        [0x11] = new(0x11, "Throttle position", "%", 0, 100, "teal"),
        [0x14] = new(0x14, "O2 sensor voltage", "V", 0, 1.275, "teal"),
    };

    public static MetricDescriptor For(byte pid) =>
        Map.TryGetValue(pid, out var d) ? d : new MetricDescriptor(pid, $"PID {pid:X2}", "", 0, 255, "teal");

    public static bool IsKnown(byte pid) => Map.ContainsKey(pid);
}
