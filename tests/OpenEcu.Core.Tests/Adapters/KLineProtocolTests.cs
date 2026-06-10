using System.IO;
using FluentAssertions;
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Protocol;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Adapters;

public class KLineProtocolTests
{
    private const KLineMode Mode = KLineMode.Kwp2000;

    private static (KLineProtocol adapter, SimulatedTransport transport) NewAdapter()
    {
        var transport = new SimulatedTransport();
        transport.OpenAsync().GetAwaiter().GetResult();
        return (new KLineProtocol(transport, Mode), transport);
    }

    [Fact]
    public async Task RequestAsync_writes_framed_request_and_returns_positive_response()
    {
        var (adapter, transport) = NewAdapter();
        // Script a positive response carrying SID 0x61 + data.
        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0x61, 0xAB }, Mode));

        EcuResponse response = await adapter.RequestAsync(new byte[] { 0x21, 0x80 });

        transport.Written.Should().Equal(KLineFrameBuilder.BuildRequest(new byte[] { 0x21, 0x80 }, Mode));
        response.IsPositive.Should().BeTrue();
        response.ServiceId.Should().Be(0x61);
        response.Data.Should().Equal(0xAB);
    }

    [Fact]
    public async Task RequestAsync_returns_negative_response_without_throwing()
    {
        var (adapter, transport) = NewAdapter();
        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0x7F, 0x21, 0x11 }, Mode));

        EcuResponse response = await adapter.RequestAsync(new byte[] { 0x21, 0x80 });

        response.IsPositive.Should().BeFalse();
        response.ServiceId.Should().Be(0x21);
        response.NegativeResponseCode.Should().Be(0x11);
    }

    [Fact]
    public async Task RequestAsync_throws_on_bad_checksum()
    {
        var (adapter, transport) = NewAdapter();
        byte[] frame = KLineFrameBuilder.BuildRequest(new byte[] { 0x61 }, Mode);
        frame[^1] ^= 0xFF; // corrupt the checksum
        transport.EnqueueResponse(frame);

        var act = async () => await adapter.RequestAsync(new byte[] { 0x21, 0x80 });
        await act.Should().ThrowAsync<InvalidDataException>();
    }
}
