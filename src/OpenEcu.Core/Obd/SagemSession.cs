using OpenEcu.Core.Adapters;
using OpenEcu.Core.Memory;
using OpenEcu.Core.Security;
using OpenEcu.Core.Transport;

namespace OpenEcu.Core.Obd;

/// <summary>
/// The Sagem tuning session over K-line. Reuses KLineObdSession for 5-baud init + framing,
/// and adds StartDiagnosticSession + SecurityAccess (seed-key unlock). Kept separate from the
/// read-only KLineObdSession because the service set differs. Unlock only — no writes.
/// </summary>
public sealed class SagemSession : IAsyncDisposable
{
    private readonly KLineObdSession _channel;
    private readonly SagemSecurityAccess _security;
    private readonly SagemMemoryReader _memory;

    public SagemSession(IEcuTransport transport, IBreakLine breakLine,
        byte initAddress = 0x33, Func<TimeSpan, Task>? delay = null)
    {
        _channel = new KLineObdSession(transport, breakLine, initAddress, delay);
        _security = new SagemSecurityAccess(_channel);
        _memory = new SagemMemoryReader(_channel);
    }

    public bool IsConnected => _channel.IsConnected;

    public Task ConnectAsync(CancellationToken ct = default) => _channel.ConnectAsync(ct);

    /// <summary>KWP StartDiagnosticSession (31 90 11, Sagem read mode). Returns the raw reply for inspection.</summary>
    public Task<ObdResponse> StartDiagnosticAsync(CancellationToken ct = default)
        => _channel.RequestAsync(new byte[] { 0x31, 0x90, 0x11 }, ct);

    /// <summary>Seed-key unlock of the ECU's tuning resources.</summary>
    public Task UnlockAsync(SecurityLevel level = SecurityLevel.Read, CancellationToken ct = default)
        => _security.UnlockAsync(level, ct);

    /// <summary>Reads <paramref name="length"/> bytes from <paramref name="address"/> into an addressable image.</summary>
    public async Task<MemoryImage> ReadMemoryAsync(int address, int length, int blockSize = 32, CancellationToken ct = default)
    {
        byte[] bytes = await _memory.ReadMemoryAsync(address, length, blockSize, ct);
        return new MemoryImage(address, bytes);
    }

    public ValueTask DisposeAsync() => _channel.DisposeAsync();
}
