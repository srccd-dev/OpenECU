using FluentAssertions;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Transport;

public class KLineEchoSuppressorTests
{
    [Fact]
    public async Task Write_drains_the_echoed_bytes_so_reads_see_only_the_reply()
    {
        var inner = new SimulatedTransport();
        await inner.OpenAsync();
        // On a K-line, the write is echoed back first, then the ECU reply arrives.
        inner.EnqueueResponse(new byte[] { 0x81, 0xD5, 0xF5, 0x81, 0xCC }); // echo of the request
        inner.EnqueueResponse(new byte[] { 0xC1, 0xEA, 0x8F });             // the reply

        var suppressor = new KLineEchoSuppressor(inner);
        await suppressor.WriteAsync(new byte[] { 0x81, 0xD5, 0xF5, 0x81, 0xCC });

        inner.Written.Should().Equal(0x81, 0xD5, 0xF5, 0x81, 0xCC);

        var buffer = new byte[3];
        int n = await suppressor.ReadAsync(buffer);
        n.Should().Be(3);
        buffer.Should().Equal(0xC1, 0xEA, 0x8F);
    }

    [Fact]
    public async Task Write_throws_when_echo_is_incomplete()
    {
        var inner = new SimulatedTransport();
        await inner.OpenAsync();
        inner.EnqueueResponse(new byte[] { 0x81, 0xD5 }); // only 2 of 5 echo bytes

        var suppressor = new KLineEchoSuppressor(inner);
        var act = async () => await suppressor.WriteAsync(new byte[] { 0x81, 0xD5, 0xF5, 0x81, 0xCC });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task IsOpen_and_lifecycle_delegate_to_inner()
    {
        var inner = new SimulatedTransport();
        var suppressor = new KLineEchoSuppressor(inner);

        suppressor.IsOpen.Should().BeFalse();
        await suppressor.OpenAsync();
        suppressor.IsOpen.Should().BeTrue();
        await suppressor.CloseAsync();
        suppressor.IsOpen.Should().BeFalse();
    }
}
