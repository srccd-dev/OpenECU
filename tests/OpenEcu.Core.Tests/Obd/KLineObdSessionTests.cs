using AwesomeAssertions;
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class KLineObdSessionTests
{
    private static readonly Func<TimeSpan, Task> NoDelay = _ => Task.CompletedTask;

    [Fact]
    public async Task RequestAsync_sends_obd_frame_and_parses_response()
    {
        // RPM request 01 0C -> real response 48 6B D1 41 0C 00 00 D1
        var ecu = new FakeEcu(new()
        {
            ["010C"] = new byte[] { 0x48, 0x6B, 0xD1, 0x41, 0x0C, 0x00, 0x00, 0xD1 },
        }, connected: true);
        await ecu.OpenAsync();
        var session = new KLineObdSession(ecu, ecu, delay: NoDelay);

        ObdResponse resp = await session.RequestAsync(new byte[] { 0x01, 0x0C });

        resp.ServiceId.Should().Be(0x41);
        resp.Payload.Should().Equal(0x0C, 0x00, 0x00);
    }

    [Fact]
    public async Task ConnectAsync_runs_init_then_completes_the_keyword_handshake()
    {
        var ecu = new FakeEcu(new(), connected: false); // serves 00 00 55 08 08, then CC after ~KW2
        await ecu.OpenAsync();
        var session = new KLineObdSession(ecu, ecu, delay: NoDelay);

        await session.ConnectAsync();

        session.IsConnected.Should().BeTrue();
        ecu.BreakToggles.Should().HaveCount(11); // 5-baud init drove 11 bit-periods
    }

    [Fact]
    public async Task ConnectAsync_throws_if_no_sync_byte_arrives()
    {
        // No 0x55 in the stream: emulate by pre-connecting (rx empty) so reads return idle.
        var ecu = new FakeEcu(new(), connected: true);
        await ecu.OpenAsync();
        var session = new KLineObdSession(ecu, ecu, delay: NoDelay);

        var act = async () => await session.ConnectAsync();

        await act.Should().ThrowAsync<EcuConnectionException>();
    }
}
