using AwesomeAssertions;
using OpenEcu.Core.Obd;
using OpenEcu.Core.Security;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class SagemSessionTests
{
    private static Task NoDelay(TimeSpan _) => Task.CompletedTask;

    [Fact]
    public async Task StartDiagnostic_then_Unlock_completes_the_seed_key_handshake()
    {
        // Response frames are 48 6B D1 <sid> <payload...> <additive checksum mod 256>.
        var ecu = new FakeEcu(new()
        {
            ["319011"]     = new byte[] { 0x48, 0x6B, 0xD1, 0x71, 0x90, 0x85 },                   // start-diag ack
            ["270302"]     = new byte[] { 0x48, 0x6B, 0xD1, 0x67, 0x03, 0x02, 0x12, 0x34, 0x36 }, // seed 0x1234
            ["270302A9D4"] = new byte[] { 0x48, 0x6B, 0xD1, 0x67, 0x03, 0x02, 0xF0 },             // granted
        }, connected: true);
        await ecu.OpenAsync();
        await using var sagem = new SagemSession(ecu, ecu, delay: NoDelay);

        ObdResponse diag = await sagem.StartDiagnosticAsync();
        diag.ServiceId.Should().Be(0x71);

        await sagem.UnlockAsync(SecurityLevel.Read); // must not throw
    }
}
