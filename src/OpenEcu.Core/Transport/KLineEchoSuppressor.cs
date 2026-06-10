namespace OpenEcu.Core.Transport;

/// <summary>
/// Decorates a transport on a single-wire K-line bus, where every transmitted byte is
/// echoed back on RX. After each write it drains exactly that many echoed bytes so the
/// next read returns only the ECU's reply.
/// </summary>
public sealed class KLineEchoSuppressor : IEcuTransport
{
    private readonly IEcuTransport _inner;

    public KLineEchoSuppressor(IEcuTransport inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public bool IsOpen => _inner.IsOpen;
    public Task OpenAsync(CancellationToken ct = default) => _inner.OpenAsync(ct);
    public Task CloseAsync() => _inner.CloseAsync();
    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _inner.ReadAsync(buffer, ct);
    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        await _inner.WriteAsync(data, ct);
        await DrainEchoAsync(data.Length, ct);
    }

    private async Task DrainEchoAsync(int count, CancellationToken ct)
    {
        byte[] scratch = new byte[count];
        int got = 0;
        while (got < count)
        {
            int n = await _inner.ReadAsync(scratch.AsMemory(got, count - got), ct);
            if (n == 0)
                throw new InvalidOperationException($"Expected a {count}-byte echo but received {got}.");
            got += n;
        }
    }
}
