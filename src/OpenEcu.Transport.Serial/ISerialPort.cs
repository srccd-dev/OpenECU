namespace OpenEcu.Transport.Serial;

/// <summary>
/// Minimal abstraction over a serial port, so SerialPortTransport can be unit-tested
/// without a physical device. The real implementation is SystemSerialPort.
/// </summary>
public interface ISerialPort : IAsyncDisposable
{
    bool IsOpen { get; }
    void Open();
    void Close();
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
    Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);
    /// <summary>Sets the break (line-low) condition. true = line held low; false = idle high.</summary>
    void SetBreak(bool on);
}
