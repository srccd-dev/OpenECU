using OpenEcu.Core.Transport;

namespace OpenEcu.Core.Protocol;

/// <summary>Reads exactly one complete K-line frame from a transport, using its length field.</summary>
public static class KLineFrameReader
{
    public static async Task<byte[]> ReadFrameAsync(
        IEcuTransport transport, KLineMode mode, CancellationToken ct = default)
    {
        int headerLen = mode == KLineMode.Kwp2000 ? 4 : 3;
        byte[] header = await ReadExactAsync(transport, headerLen, ct);

        int payloadLen = mode == KLineMode.Kwp2000 ? header[3] : (header[0] & 0x3F);

        byte[] rest = await ReadExactAsync(transport, payloadLen + 1, ct); // payload + checksum

        byte[] frame = new byte[headerLen + payloadLen + 1];
        header.CopyTo(frame.AsSpan(0));
        rest.CopyTo(frame.AsSpan(headerLen));
        return frame;
    }

    private static async Task<byte[]> ReadExactAsync(IEcuTransport transport, int count, CancellationToken ct)
    {
        byte[] buffer = new byte[count];
        int got = 0;
        while (got < count)
        {
            int n = await transport.ReadAsync(buffer.AsMemory(got, count - got), ct);
            if (n == 0)
                throw new IncompleteFrameException($"Stream ended after {got} of {count} expected bytes.");
            got += n;
        }
        return buffer;
    }
}
