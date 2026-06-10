namespace OpenEcu.Core.Protocol;

/// <summary>
/// ISO9141 5-baud slow-init bit pattern. Reproduces the original tool's encoding:
/// pattern = address*4 + 1025, taken LSB-first over 11 bit-periods. Each returned bool is
/// the BREAK-ON state for that period: break is ON (line held LOW) when the pattern bit is
/// 0, OFF (line HIGH) when the bit is 1 — matching ISOFT.SetBreak (v==0 => break on).
/// The frame therefore reads: lead-in HIGH, START LOW, 8 data bits LSB-first, STOP HIGH.
/// </summary>
public static class FiveBaudInitPattern
{
    public const int BitCount = 11;

    public static bool[] BreakStatesFor(byte address)
    {
        int pattern = address * 4 + 1025;
        var states = new bool[BitCount];
        for (int i = 0; i < BitCount; i++)
        {
            states[i] = (pattern & 1) == 0; // break ON (line low) when the bit is 0
            pattern >>= 1;
        }
        return states;
    }
}
