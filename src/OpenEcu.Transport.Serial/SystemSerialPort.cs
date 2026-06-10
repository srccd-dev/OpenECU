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

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        => _port.BaseStream.WriteAsync(data, ct).AsTask();

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        => _port.BaseStream.ReadAsync(buffer, ct).AsTask();

    public ValueTask DisposeAsync()
    {
        _port.Dispose();
        return ValueTask.CompletedTask;
    }
}
