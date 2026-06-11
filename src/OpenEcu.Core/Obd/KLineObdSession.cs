using System.IO;
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Transport;

namespace OpenEcu.Core.Obd;

/// <summary>
/// A live ISO9141-2 OBD-II session over a K-line cable: 5-baud init, keyword handshake,
/// echo-locked transmit, read-until-idle, and OBD decode. Caller opens the transport first.
/// </summary>
public sealed class KLineObdSession : IObdSession, IObdRequestChannel
{
    private readonly IEcuTransport _transport;
    private readonly IBreakLine _breakLine;
    private readonly byte _initAddress;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly KLineFiveBaudInitializer _initializer;

    public KLineObdSession(IEcuTransport transport, IBreakLine breakLine,
        byte initAddress = 0x33, Func<TimeSpan, Task>? delay = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _breakLine = breakLine ?? throw new ArgumentNullException(nameof(breakLine));
        _initAddress = initAddress;
        _delay = delay ?? Task.Delay;
        _initializer = new KLineFiveBaudInitializer(delay: _delay);
    }

    public bool IsConnected { get; private set; }

    public async Task<ObdResponse> RequestAsync(byte[] payload, CancellationToken ct = default)
    {
        byte[] frame = ObdMessage.BuildRequest(payload);
        await SendEchoLockedAsync(frame, ct);
        byte[] responseBytes = await ReadUntilIdleAsync(ct);
        if (!ObdMessage.TryParseResponse(responseBytes, out ObdResponse response))
            throw new InvalidDataException(
                $"Invalid OBD response: {BitConverter.ToString(responseBytes)}");
        return response;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _initializer.InitializeAsync(_breakLine, _initAddress, ct);

        // After init the ECU sends some line-settling noise, then 0x55 sync + 2 keyword bytes.
        int sync = await ReadUntilAsync(0x55, ct);
        if (sync < 0)
            throw new EcuConnectionException("No 0x55 sync byte after 5-baud init.");

        int kw1 = await ReadByteAsync(ct);
        int kw2 = await ReadByteAsync(ct);
        if (kw1 < 0 || kw2 < 0)
            throw new EcuConnectionException("Missing keyword bytes after sync.");

        await _delay(TimeSpan.FromMilliseconds(30)); // W4
        byte invKw2 = (byte)(kw2 ^ 0xFF);
        await _transport.WriteAsync(new[] { invKw2 }, ct);

        // Skip the echo of our ~KW2; the next distinct byte is the inverted address (~addr).
        int invAddr = await ReadSkippingAsync(invKw2, ct);
        if (invAddr < 0)
            throw new EcuConnectionException("No inverted-address reply; handshake not accepted.");

        IsConnected = true;
    }
    public async Task<IReadOnlyList<byte>> ReadSupportedPidsAsync(CancellationToken ct = default)
    {
        var all = new List<byte>();
        foreach (byte basePid in new byte[] { 0x00, 0x20, 0x40 })
        {
            ObdResponse resp = await RequestAsync(new byte[] { 0x01, basePid }, ct);
            // Payload = [echoed pid, 4 bitmask bytes]
            if (resp.ServiceId != 0x41 || resp.Payload.Length < 5 || resp.Payload[0] != basePid)
                break;
            IReadOnlyList<byte> pids = SupportedPids.Parse(basePid, resp.Payload.AsSpan(1, 4));
            all.AddRange(pids);
            if (!pids.Contains((byte)(basePid + 0x20)))
                break; // next 32-PID range not advertised
        }
        return all;
    }

    public async Task<PidReading> ReadPidAsync(byte pid, CancellationToken ct = default)
    {
        ObdResponse resp = await RequestAsync(new byte[] { 0x01, pid }, ct);
        if (resp.ServiceId != 0x41 || resp.Payload.Length < 1 || resp.Payload[0] != pid)
            throw new InvalidDataException($"Unexpected response to PID 0x{pid:X2}.");
        // Extract to a local array first: ReadOnlySpan<byte> is a ref struct, can't live across await.
        byte[] pidData = resp.Payload[1..];
        return PidDecoder.Decode(pid, pidData);
    }

    public async Task<IReadOnlyList<string>> ReadDtcsAsync(CancellationToken ct = default)
    {
        ObdResponse resp = await RequestAsync(new byte[] { 0x03 }, ct);
        if (resp.ServiceId != 0x43)
            throw new InvalidDataException("Unexpected Mode 03 response.");
        return DtcDecoder.Decode(resp.Payload);
    }

    public async Task ClearDtcsAsync(CancellationToken ct = default)
    {
        ObdResponse resp = await RequestAsync(new byte[] { 0x04 }, ct);
        if (resp.ServiceId != 0x44)
            throw new InvalidDataException($"Mode 04 (clear codes) rejected: SID 0x{resp.ServiceId:X2}.");
    }

    public ValueTask DisposeAsync() => _transport.DisposeAsync();

    // Sends each byte then waits for its echo (the single-wire K-line echoes TX).
    private async Task SendEchoLockedAsync(byte[] frame, CancellationToken ct)
    {
        foreach (byte tx in frame)
        {
            await _transport.WriteAsync(new[] { tx }, ct);
            int echo = await ReadByteAsync(ct);
            if (echo != tx)
                throw new InvalidDataException($"Echo mismatch: sent 0x{tx:X2}, read 0x{echo:X2}.");
        }
    }

    // Reads bytes until the idle gap (a zero-length read).
    private async Task<byte[]> ReadUntilIdleAsync(CancellationToken ct)
    {
        var bytes = new List<byte>();
        while (true)
        {
            int b = await ReadByteAsync(ct);
            if (b < 0) break;
            bytes.Add((byte)b);
        }
        return bytes.ToArray();
    }

    private async Task<int> ReadByteAsync(CancellationToken ct)
    {
        var buffer = new byte[1];
        int n = await _transport.ReadAsync(buffer, ct);
        return n > 0 ? buffer[0] : -1;
    }

    // Reads (discarding) until the target byte appears; -1 if the stream goes idle first.
    private async Task<int> ReadUntilAsync(byte target, CancellationToken ct)
    {
        while (true)
        {
            int b = await ReadByteAsync(ct);
            if (b < 0) return -1;
            if (b == target) return b;
        }
    }

    // Reads until a byte that is NOT skip; -1 if the stream goes idle first.
    private async Task<int> ReadSkippingAsync(byte skip, CancellationToken ct)
    {
        while (true)
        {
            int b = await ReadByteAsync(ct);
            if (b < 0) return -1;
            if (b != skip) return b;
        }
    }
}
