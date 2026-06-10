namespace OpenEcu.Core.Obd;

/// <summary>Decodes standard OBD-II Mode 01 PIDs into physical values.</summary>
public static class PidDecoder
{
    public static PidReading Decode(byte pid, ReadOnlySpan<byte> data)
    {
        byte[] raw = data.ToArray();
        int A = data.Length > 0 ? data[0] : 0;
        int B = data.Length > 1 ? data[1] : 0;

        return pid switch
        {
            0x04 => new PidReading(pid, "Calculated engine load", A * 100.0 / 255, "%", raw),
            0x05 => new PidReading(pid, "Coolant temperature", A - 40, "C", raw),
            0x06 => new PidReading(pid, "Short-term fuel trim", (A - 128) * 100.0 / 128, "%", raw),
            0x07 => new PidReading(pid, "Long-term fuel trim", (A - 128) * 100.0 / 128, "%", raw),
            0x0B => new PidReading(pid, "Intake manifold pressure", A, "kPa", raw),
            0x0C => new PidReading(pid, "Engine RPM", (A * 256 + B) / 4.0, "rpm", raw),
            0x0D => new PidReading(pid, "Vehicle speed", A, "km/h", raw),
            0x0E => new PidReading(pid, "Timing advance", A / 2.0 - 64, "deg", raw),
            0x0F => new PidReading(pid, "Intake air temperature", A - 40, "C", raw),
            0x11 => new PidReading(pid, "Throttle position", A * 100.0 / 255, "%", raw),
            0x14 => new PidReading(pid, "O2 sensor voltage", A * 0.005, "V", raw),
            _ => new PidReading(pid, $"PID {pid:X2}", null, "", raw),
        };
    }
}
