using OpenEcu.Core.Obd;

namespace OpenEcu.Core.Memory;

/// <summary>
/// Reads ECU memory over an IObdRequestChannel using KWP ReadMemoryByAddress (0x23):
/// 23 A2 A1 A0 LEN 00 -> 63 &lt;LEN bytes&gt;. Bulk reads loop in blockSize chunks,
/// incrementing the address, and concatenate. Throws MemoryReadException on 0x7F or a
/// malformed block. Pure protocol logic — no serial knowledge.
/// </summary>
public sealed class SagemMemoryReader
{
    private readonly IObdRequestChannel _channel;

    public SagemMemoryReader(IObdRequestChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<byte[]> ReadMemoryAsync(int address, int length, int blockSize = 32, CancellationToken ct = default)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (blockSize is <= 0 or > 255) throw new ArgumentOutOfRangeException(nameof(blockSize));

        var result = new byte[length];
        int done = 0;
        while (done < length)
        {
            int blockAddr = address + done;
            int blockLen = Math.Min(blockSize, length - done);
            ObdResponse resp = await _channel.RequestAsync(
                new byte[] { 0x23, (byte)(blockAddr >> 16), (byte)(blockAddr >> 8), (byte)blockAddr, (byte)blockLen, 0x00 }, ct);

            if (resp.ServiceId == 0x7F)
            {
                byte nrc = resp.Payload.Length >= 2 ? resp.Payload[1] : (byte)0;
                throw new MemoryReadException(nrc, blockAddr,
                    $"ReadMemoryByAddress rejected at 0x{blockAddr:X6} (NRC 0x{nrc:X2}).");
            }
            if (resp.ServiceId != 0x63 || resp.Payload.Length < blockLen)
                throw new MemoryReadException(0, blockAddr,
                    $"Unexpected read response at 0x{blockAddr:X6}: SID 0x{resp.ServiceId:X2}, {resp.Payload.Length} bytes (wanted {blockLen}).");

            Array.Copy(resp.Payload, 0, result, done, blockLen);
            done += blockLen;
        }
        return result;
    }
}
