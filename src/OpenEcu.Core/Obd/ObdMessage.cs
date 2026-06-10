namespace OpenEcu.Core.Obd;

/// <summary>
/// Builds and parses ISO9141-2 OBD-II K-line messages for an ECU in generic OBD mode.
/// Request header is 68 6A F1; response header is 48 6B &lt;ecu&gt;. The trailing byte of
/// every frame is the sum of all preceding bytes, mod 256.
/// </summary>
public static class ObdMessage
{
    private const byte ReqFormat = 0x68;  // functional OBD request
    private const byte ReqTarget = 0x6A;  // ECU
    private const byte ReqSource = 0xF1;  // tester
    private const int ResponseHeaderLength = 3; // fmt, target, source

    public static byte[] BuildRequest(ReadOnlySpan<byte> payload)
    {
        byte[] frame = new byte[3 + payload.Length + 1];
        frame[0] = ReqFormat;
        frame[1] = ReqTarget;
        frame[2] = ReqSource;
        payload.CopyTo(frame.AsSpan(3));
        frame[^1] = Checksum(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }

    public static bool TryParseResponse(ReadOnlySpan<byte> frame, out ObdResponse response)
    {
        response = null!;
        // header(3) + service id(1) + checksum(1) minimum
        if (frame.Length < ResponseHeaderLength + 2)
            return false;
        if (frame[^1] != Checksum(frame[..^1]))
            return false;

        byte serviceId = frame[ResponseHeaderLength];
        byte[] payload = frame[(ResponseHeaderLength + 1)..^1].ToArray();
        response = new ObdResponse(serviceId, payload);
        return true;
    }

    private static byte Checksum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        foreach (byte b in data) sum += b;
        return (byte)sum;
    }
}
