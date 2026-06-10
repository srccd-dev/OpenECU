using AwesomeAssertions;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Transport;

public class LoggingTransportTests
{
    [Fact]
    public async Task Raises_BytesWritten_and_passes_through_to_inner()
    {
        var inner = new SimulatedTransport();
        await inner.OpenAsync();
        var log = new LoggingTransport(inner);
        byte[]? seen = null;
        log.BytesWritten += b => seen = b;

        await log.WriteAsync(new byte[] { 0x01, 0x02 });

        seen.Should().Equal(0x01, 0x02);
        inner.Written.Should().Equal(0x01, 0x02);
    }

    [Fact]
    public async Task Raises_BytesRead_with_only_the_bytes_actually_read()
    {
        var inner = new SimulatedTransport();
        inner.EnqueueResponse(new byte[] { 0xAA, 0xBB });
        await inner.OpenAsync();
        var log = new LoggingTransport(inner);
        byte[]? seen = null;
        log.BytesRead += b => seen = b;

        var buffer = new byte[8];
        int n = await log.ReadAsync(buffer);

        n.Should().Be(2);
        seen.Should().Equal(0xAA, 0xBB); // not the full 8-byte buffer
    }

    [Fact]
    public async Task Does_not_raise_BytesRead_on_empty_read()
    {
        var inner = new SimulatedTransport();
        await inner.OpenAsync();
        var log = new LoggingTransport(inner);
        bool raised = false;
        log.BytesRead += _ => raised = true;

        int n = await log.ReadAsync(new byte[4]);

        n.Should().Be(0);
        raised.Should().BeFalse();
    }

    [Fact]
    public void IsOpen_reflects_inner()
    {
        var inner = new SimulatedTransport();
        new LoggingTransport(inner).IsOpen.Should().Be(inner.IsOpen);
    }
}
