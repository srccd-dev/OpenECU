using OpenEcu.Core.Adapters;
using OpenEcu.Core.Transport;

namespace OpenEcu.Core.Tests.Obd;

/// <summary>
/// In-memory ISO9141-2 OBD bike emulator. Echoes every written byte (single-wire K-line),
/// answers complete OBD request frames from a scripted table, and (when not pre-connected)
/// serves the 5-baud handshake: noise + sync + keywords, then the inverted address after
/// the first write (the tester's ~KW2). A zero-length read signals the idle gap.
/// </summary>
public sealed class FakeEcu : IEcuTransport, IBreakLine
{
    private readonly Queue<byte> _rx = new();
    private readonly List<byte> _frame = new();
    private readonly Dictionary<string, byte[]> _responses;
    private bool _handshakeReplied;

    public List<bool> BreakToggles { get; } = new();
    public bool IsOpen { get; private set; }

    /// <param name="responses">payload (hex, e.g. "010C") -> full response frame bytes.</param>
    /// <param name="connected">true to skip the handshake (test RequestAsync directly).</param>
    public FakeEcu(Dictionary<string, byte[]> responses, bool connected = false)
    {
        _responses = responses;
        _handshakeReplied = connected;
        if (!connected)
            foreach (byte b in new byte[] { 0x00, 0x00, 0x55, 0x08, 0x08 })
                _rx.Enqueue(b);
    }

    public void SetBreak(bool on) => BreakToggles.Add(on);
    public Task OpenAsync(CancellationToken ct = default) { IsOpen = true; return Task.CompletedTask; }
    public Task CloseAsync() { IsOpen = false; return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        foreach (byte b in data.ToArray())
        {
            _rx.Enqueue(b); // K-line echoes the transmitted byte
            if (!_handshakeReplied)
            {
                _rx.Enqueue(0xCC); // ~addr reply to the tester's ~KW2
                _handshakeReplied = true;
                continue;
            }
            _frame.Add(b);
            if (IsCompleteObdFrame(_frame))
            {
                byte[] payload = _frame.GetRange(3, _frame.Count - 4).ToArray(); // after 68 6A F1, before checksum
                if (_responses.TryGetValue(Convert.ToHexString(payload), out byte[]? resp))
                    foreach (byte rb in resp) _rx.Enqueue(rb);
                _frame.Clear();
            }
        }
        return Task.CompletedTask;
    }

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_rx.Count == 0) return Task.FromResult(0);
        buffer.Span[0] = _rx.Dequeue();
        return Task.FromResult(1);
    }

    private static bool IsCompleteObdFrame(List<byte> f)
    {
        if (f.Count < 5 || f[0] != 0x68) return false;
        int sum = 0;
        for (int i = 0; i < f.Count - 1; i++) sum += f[i];
        return (byte)sum == f[^1];
    }
}
