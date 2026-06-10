using FluentAssertions;
using OpenEcu.Core.Transport;
using OpenEcu.Transport.Serial;
using Xunit;

namespace OpenEcu.Transport.Serial.Tests;

public class ManualHardwareTests
{
    // The FTDI cable enumerates as a COM port. Find yours via Device Manager or by calling
    // SerialPortEnumerator.GetPortNames(). On the author's machine it is COM8.
    private const string PortName = "COM8";

    [Fact(Skip = "Manual: requires the FTDI KKL cable plugged in. Set PortName, then remove Skip and run.")]
    public async Task Can_open_write_and_close_the_real_cable()
    {
        await using var port = new SystemSerialPort(PortName, baudRate: 10400, readTimeoutMs: 1000, writeTimeoutMs: 1000);
        IEcuTransport transport = new SerialPortTransport(port);

        await transport.OpenAsync();
        transport.IsOpen.Should().BeTrue();

        // Writing is safe even with nothing on the K-line; this just exercises the path.
        await transport.WriteAsync(new byte[] { 0x00 });

        await transport.CloseAsync();
        transport.IsOpen.Should().BeFalse();
    }
}
