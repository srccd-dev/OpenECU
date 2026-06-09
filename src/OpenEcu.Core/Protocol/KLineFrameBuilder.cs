namespace OpenEcu.Core.Protocol;

/// <summary>Builds tester→ECU request frames. See docs/protocol/kline.md.</summary>
public static class KLineFrameBuilder
{
    public const byte TargetEcu = 0xD5;
    public const byte SourceTester = 0xF5;
    private const byte FormatBase = 0x80;
    private const int MaxIsoPayload = 63; // 6-bit length field in the format byte

    public static byte[] BuildRequest(ReadOnlySpan<byte> payload, KLineMode mode)
    {
        if (mode == KLineMode.Iso9141 && payload.Length > MaxIsoPayload)
            throw new ArgumentException(
                $"ISO9141 payload must be <= {MaxIsoPayload} bytes, was {payload.Length}.",
                nameof(payload));

        int headerLen = mode == KLineMode.Kwp2000 ? 4 : 3;
        byte[] frame = new byte[headerLen + payload.Length + 1];

        frame[0] = (byte)(mode == KLineMode.Kwp2000 ? FormatBase : FormatBase | payload.Length);
        frame[1] = TargetEcu;
        frame[2] = SourceTester;
        if (mode == KLineMode.Kwp2000)
            frame[3] = (byte)payload.Length;

        payload.CopyTo(frame.AsSpan(headerLen));
        frame[^1] = KLineChecksum.Calculate(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }
}
