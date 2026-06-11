using System.Text;
using AwesomeAssertions;
using OpenEcu.Core.Obd;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class Elm327ObdSessionTests
{
    // Scripts a SimulatedTransport with ELM327 ASCII replies (each ends with the '>' prompt).
    private static async Task<SimulatedTransport> Open(params string[] replies)
    {
        var t = new SimulatedTransport();
        await t.OpenAsync();
        foreach (string r in replies)
            t.EnqueueResponse(Encoding.ASCII.GetBytes(r + "\r>"));
        return t;
    }

    [Fact]
    public async Task ConnectAsync_runs_at_setup_then_confirms_with_0100()
    {
        // 6 AT commands then 0100.
        var t = await Open("ELM327 v1.5", "OK", "OK", "OK", "OK", "OK", "4100BE1E9011");
        var s = new Elm327ObdSession(t);

        await s.ConnectAsync();

        s.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task ReadPidAsync_decodes_rpm()
    {
        var t = await Open("410C0BB8"); // 0x0BB8 / 4 = 750 rpm
        var s = new Elm327ObdSession(t);

        PidReading r = await s.ReadPidAsync(0x0C);

        r.Value.Should().Be(750);
    }

    [Fact]
    public async Task ReadDtcsAsync_decodes_codes()
    {
        var t = await Open("431502");
        var s = new Elm327ObdSession(t);

        var dtcs = await s.ReadDtcsAsync();

        dtcs.Should().Equal("P1502");
    }

    [Fact]
    public async Task ReadSupportedPidsAsync_parses_the_bitmask()
    {
        // 0100 -> supported; 0x20 set so it asks 0120; then stop.
        var t = await Open("4100BE1E9011", "412000000001", "414000000000");
        var s = new Elm327ObdSession(t);

        var pids = await s.ReadSupportedPidsAsync();

        pids.Should().Contain(new byte[] { 0x0C, 0x05, 0x11 });
    }
}
