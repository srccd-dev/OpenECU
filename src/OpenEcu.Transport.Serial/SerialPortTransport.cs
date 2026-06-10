using OpenEcu.Core.Transport;

namespace OpenEcu.Transport.Serial;

/// <summary>An IEcuTransport backed by a serial (Virtual COM Port) device.</summary>
public sealed class SerialPortTransport : IEcuTransport
{
    private readonly ISerialPort _port;

    public SerialPortTransport(ISerialPort port)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
    }

    public bool IsOpen => _port.IsOpen;

    public Task OpenAsync(CancellationToken ct = default)
    {
        _port.Open();
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        _port.Close();
        return Task.CompletedTask;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        EnsureOpen();
        await _port.WriteAsync(data, ct);
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        EnsureOpen();
        return await _port.ReadAsync(buffer, ct);
    }

    public ValueTask DisposeAsync() => _port.DisposeAsync();

    private void EnsureOpen()
    {
        if (!_port.IsOpen)
            throw new InvalidOperationException("Serial port is not open.");
    }
}
