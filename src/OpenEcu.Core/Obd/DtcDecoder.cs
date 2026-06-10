namespace OpenEcu.Core.Obd;

/// <summary>Decodes Mode 03/07 trouble-code byte pairs into DTC strings (e.g. "P1502").</summary>
public static class DtcDecoder
{
    private const string SystemLetters = "PCBU";

    /// <param name="payload">DTC byte pairs (the Mode 03 data after service id 0x43).</param>
    public static IReadOnlyList<string> Decode(ReadOnlySpan<byte> payload)
    {
        var codes = new List<string>();
        for (int i = 0; i + 1 < payload.Length; i += 2)
        {
            int a = payload[i];
            int b = payload[i + 1];
            if (a == 0 && b == 0)
                continue; // empty slot

            char system = SystemLetters[(a >> 6) & 0x3];
            codes.Add($"{system}{(a >> 4) & 0x3}{a & 0xF:X}{b >> 4:X}{b & 0xF:X}");
        }
        return codes;
    }
}
