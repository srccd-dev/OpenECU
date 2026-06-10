using System.IO;
using System.Linq;
using AwesomeAssertions;
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

    [Fact]
    public async Task ConnectAsync_sends_StartCommunication_and_sets_connected()
    {
        var (adapter, transport) = NewAdapter();
        // Positive StartCommunication response: 0xC1 + two key bytes.
        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0xC1, 0xEA, 0x8F }, Mode));

        await adapter.ConnectAsync();

        adapter.IsConnected.Should().BeTrue();
        transport.Written.Should().Equal(KLineFrameBuilder.BuildRequest(new byte[] { 0x81 }, Mode));
    }

    [Fact]
    public async Task ConnectAsync_throws_and_stays_disconnected_on_negative_response()
    {
        var (adapter, transport) = NewAdapter();
        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0x7F, 0x81, 0x10 }, Mode));

        var act = async () => await adapter.ConnectAsync();

        await act.Should().ThrowAsync<EcuConnectionException>();
        adapter.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task DisconnectAsync_sends_StopCommunication_and_clears_connected()
    {
        var (adapter, transport) = NewAdapter();
        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0xC1, 0xEA, 0x8F }, Mode));
        await adapter.ConnectAsync();

        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0xC2 }, Mode));
        await adapter.DisconnectAsync();

        adapter.IsConnected.Should().BeFalse();
        // Second write (index after the connect frame) is the StopCommunication request.
        var stopFrame = KLineFrameBuilder.BuildRequest(new byte[] { 0x82 }, Mode);
        transport.Written.Skip(KLineFrameBuilder.BuildRequest(new byte[] { 0x81 }, Mode).Length)
                  .Should().Equal(stopFrame);
    }

    [Fact]
    public async Task TesterPresentAsync_sends_3E_request()
    {
        var (adapter, transport) = NewAdapter();
        transport.EnqueueResponse(KLineFrameBuilder.BuildRequest(new byte[] { 0x7E }, Mode));

        await adapter.TesterPresentAsync();

        transport.Written.Should().Equal(KLineFrameBuilder.BuildRequest(new byte[] { 0x3E }, Mode));
    }
}
