namespace OpenEcu.Core.Obd;

/// <summary>Decodes a "PIDs supported" bitmask (Mode 01 PID 00/20/40 data) into PID numbers.</summary>
public static class SupportedPids
{
    /// <param name="basePid">The query PID (0x00, 0x20, 0x40). Results are offset by it.</param>
    /// <param name="bitmask">The 4 data bytes; MSB of byte 0 is basePid+1.</param>
    public static IReadOnlyList<byte> Parse(byte basePid, ReadOnlySpan<byte> bitmask)
    {
        if (bitmask.Length != 4)
            throw new ArgumentException("Supported-PID bitmask must be exactly 4 bytes.", nameof(bitmask));

        var pids = new List<byte>();
        for (int i = 0; i < 32; i++)
        {
            bool supported = (bitmask[i / 8] & (0x80 >> (i % 8))) != 0;
            if (supported)
                pids.Add((byte)(basePid + i + 1));
        }
        return pids;
    }
}
