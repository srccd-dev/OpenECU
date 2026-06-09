using FluentAssertions;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Transport;

public class SimulatedTransportTests
{
    [Fact]
    public async Task Open_then_close_toggles_IsOpen()
    {
        var t = new SimulatedTransport();
        t.IsOpen.Should().BeFalse();
        await t.OpenAsync();
        t.IsOpen.Should().BeTrue();
        await t.CloseAsync();
        t.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Write_records_bytes_for_inspection()
    {
        var t = new SimulatedTransport();
        await t.OpenAsync();
        await t.WriteAsync(new byte[] { 0x01, 0x02 });
        t.Written.Should().Equal(0x01, 0x02);
    }

    [Fact]
    public async Task Read_drains_scripted_response_bytes()
    {
        var t = new SimulatedTransport();
        t.EnqueueResponse(new byte[] { 0xAA, 0xBB, 0xCC });
        await t.OpenAsync();

        var buffer = new byte[2];
        int n1 = await t.ReadAsync(buffer);
        n1.Should().Be(2);
        buffer.Should().Equal(0xAA, 0xBB);

        int n2 = await t.ReadAsync(buffer);
        n2.Should().Be(1);
        buffer[0].Should().Be(0xCC);
    }

    [Fact]
    public async Task Write_before_open_throws()
    {
        var t = new SimulatedTransport();
        var act = async () => await t.WriteAsync(new byte[] { 0x01 });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
