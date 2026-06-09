namespace OpenEcu.Core.Protocol;

/// <summary>Validates and extracts the payload from an ECU→tester response frame.</summary>
public static class KLineFrameParser
{
    public static bool TryParse(ReadOnlySpan<byte> frame, KLineMode mode, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        int headerLen = mode == KLineMode.Kwp2000 ? 4 : 3;
        int minLen = headerLen + 1; // header + at least the checksum byte
        if (frame.Length < minLen)
            return false;

        byte expected = KLineChecksum.Calculate(frame[..^1]);
        if (frame[^1] != expected)
            return false;

        payload = frame[headerLen..^1];
        return true;
    }
}
