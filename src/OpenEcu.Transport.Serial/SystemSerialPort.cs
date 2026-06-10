using System.IO.Ports;

namespace OpenEcu.Transport.Serial;

/// <summary>ISerialPort backed by System.IO.Ports.SerialPort (FTDI VCP, CH340, BT-SPP, ...).</summary>
public sealed class SystemSerialPort : ISerialPort
{
    private readonly SerialPort _port;

    /// <param name="portName">e.g. "COM8" on Windows or "/dev/ttyUSB0" on Linux.</param>
    /// <param name="baudRate">K-line default is 10400 baud.</param>
    public SystemSerialPort(string portName, int baudRate = 10400, int readTimeoutMs = 2000, int writeTimeoutMs = 2000)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = readTimeoutMs,
            WriteTimeout = writeTimeoutMs
        };
    }

    public bool IsOpen => _port.IsOpen;

    public void Open() => _port.Open();

    public void Close()
    {
        if (_port.IsOpen)
            _port.Close();
    }

    public void SetBreak(bool on) => _port.BreakState = on;

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        => _port.BaseStream.WriteAsync(data, ct).AsTask();

    // NOTE: SerialPort's *async* read does not reliably honor cancellation or ReadTimeout on
    // Windows and can hang when no data arrives. Use the synchronous read (which honors
    // ReadTimeout) on a thread-pool thread, returning 0 on timeout instead of blocking forever.
    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var temp = new byte[buffer.Length];
            try
            {
                int n = _port.Read(temp, 0, temp.Length);
                temp.AsSpan(0, n).CopyTo(buffer.Span);
                return n;
            }
            catch (TimeoutException)
            {
                return 0;
            }
        }, ct);

    public ValueTask DisposeAsync()
    {
        _port.Dispose();
        return ValueTask.CompletedTask;
    }
}
