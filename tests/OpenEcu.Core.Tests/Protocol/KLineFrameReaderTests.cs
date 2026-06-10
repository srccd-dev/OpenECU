using FluentAssertions;
using OpenEcu.Core.Protocol;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class KLineFrameReaderTests
{
    [Fact]
    public async Task Reads_one_complete_kwp_frame()
    {
        // Fabricate a valid KWP frame carrying payload C1 EA 8F.
        byte[] frame = KLineFrameBuilder.BuildRequest(new byte[] { 0xC1, 0xEA, 0x8F }, KLineMode.Kwp2000);
        var transport = new SimulatedTransport();
        transport.EnqueueResponse(frame);
        await transport.OpenAsync();

        byte[] read = await KLineFrameReader.ReadFrameAsync(transport, KLineMode.Kwp2000);
        read.Should().Equal(frame);
    }

    [Fact]
    public async Task Reads_one_complete_iso_frame()
    {
        byte[] frame = KLineFrameBuilder.BuildRequest(new byte[] { 0x41, 0x00, 0x01, 0x02 }, KLineMode.Iso9141);
        var transport = new SimulatedTransport();
        transport.EnqueueResponse(frame);
        await transport.OpenAsync();

        byte[] read = await KLineFrameReader.ReadFrameAsync(transport, KLineMode.Iso9141);
        read.Should().Equal(frame);
    }

    [Fact]
    public async Task Reads_only_the_first_frame_when_two_are_queued()
    {
        byte[] first = KLineFrameBuilder.BuildRequest(new byte[] { 0x7E }, KLineMode.Kwp2000);
        byte[] second = KLineFrameBuilder.BuildRequest(new byte[] { 0xC2 }, KLineMode.Kwp2000);
        var transport = new SimulatedTransport();
        transport.EnqueueResponse(first);
        transport.EnqueueResponse(second);
        await transport.OpenAsync();

        byte[] read = await KLineFrameReader.ReadFrameAsync(transport, KLineMode.Kwp2000);
        read.Should().Equal(first);
    }

    [Fact]
    public async Task Throws_when_stream_ends_mid_frame()
    {
        // Header claims 3 payload bytes but only 1 is provided.
        var transport = new SimulatedTransport();
        transport.EnqueueResponse(new byte[] { 0x80, 0xF5, 0xD5, 0x03, 0xC1 });
        await transport.OpenAsync();

        var act = async () => await KLineFrameReader.ReadFrameAsync(transport, KLineMode.Kwp2000);
        await act.Should().ThrowAsync<IncompleteFrameException>();
    }
}
