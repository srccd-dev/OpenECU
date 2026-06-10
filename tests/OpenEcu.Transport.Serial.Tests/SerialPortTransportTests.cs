using AwesomeAssertions;
using OpenEcu.Core.Transport;
using OpenEcu.Transport.Serial;
using Xunit;

namespace OpenEcu.Transport.Serial.Tests;

public class SerialPortTransportTests
{
    [Fact]
    public async Task OpenAsync_opens_underlying_port()
    {
        var fake = new FakeSerialPort();
        IEcuTransport transport = new SerialPortTransport(fake);

        transport.IsOpen.Should().BeFalse();
        await transport.OpenAsync();
        transport.IsOpen.Should().BeTrue();
        fake.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_delegates_to_port()
    {
        var fake = new FakeSerialPort();
        IEcuTransport transport = new SerialPortTransport(fake);
        await transport.OpenAsync();

        await transport.WriteAsync(new byte[] { 0x10, 0x20 });

        fake.Written.Should().Equal(0x10, 0x20);
    }

    [Fact]
    public async Task ReadAsync_returns_bytes_from_port()
    {
        var fake = new FakeSerialPort();
        fake.EnqueueRead(0xAA, 0xBB);
        IEcuTransport transport = new SerialPortTransport(fake);
        await transport.OpenAsync();

        var buffer = new byte[2];
        int n = await transport.ReadAsync(buffer);

        n.Should().Be(2);
        buffer.Should().Equal(0xAA, 0xBB);
    }

    [Fact]
    public async Task WriteAsync_before_open_throws()
    {
        var fake = new FakeSerialPort();
        IEcuTransport transport = new SerialPortTransport(fake);

        var act = async () => await transport.WriteAsync(new byte[] { 0x01 });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
