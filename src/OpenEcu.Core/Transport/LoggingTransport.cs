namespace OpenEcu.Core.Transport;

/// <summary>
/// Pass-through IEcuTransport decorator that raises events for every byte block written or
/// read. Used to feed a raw protocol console without coupling it to the session.
/// </summary>
public sealed class LoggingTransport : IEcuTransport
{
    private readonly IEcuTransport _inner;

    public LoggingTransport(IEcuTransport inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public event Action<byte[]>? BytesWritten;
    public event Action<byte[]>? BytesRead;

    public bool IsOpen => _inner.IsOpen;
    public Task OpenAsync(CancellationToken ct = default) => _inner.OpenAsync(ct);
    public Task CloseAsync() => _inner.CloseAsync();
    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        await _inner.WriteAsync(data, ct);
        BytesWritten?.Invoke(data.ToArray());
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        int n = await _inner.ReadAsync(buffer, ct);
        if (n > 0)
            BytesRead?.Invoke(buffer.Slice(0, n).ToArray());
        return n;
    }
}
