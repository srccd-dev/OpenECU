using AwesomeAssertions;
using OpenEcu.Core.Protocol;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class KLineRoundTripTests
{
    [Fact]
    public async Task Request_is_written_and_scripted_response_parses_back()
    {
        var transport = new SimulatedTransport();
        await transport.OpenAsync();

        // Build and send a KWP request with payload 0x81.
        byte[] request = KLineFrameBuilder.BuildRequest(new byte[] { 0x81 }, KLineMode.Kwp2000);
        await transport.WriteAsync(request);
        transport.Written.Should().Equal(0x80, 0xD5, 0xF5, 0x01, 0x81, 0xCC);

        // Script an ECU response carrying payload 0xC1, 0xEA, 0x8F (a plausible positive reply).
        byte[] response = BuildResponse(new byte[] { 0xC1, 0xEA, 0x8F });
        transport.EnqueueResponse(response);

        // Read the whole response back and parse it.
        var buffer = new byte[response.Length];
        int n = await transport.ReadAsync(buffer);
        n.Should().Be(response.Length);

        var (parsed, payloadBytes) = ParseFrame(buffer, n, KLineMode.Kwp2000);
        parsed.Should().BeTrue();
        payloadBytes.Should().Equal(0xC1, 0xEA, 0x8F);
    }

    // Sync helper to avoid ReadOnlySpan<byte> out param inside async method (C# 12 restriction).
    private static (bool ok, byte[] payload) ParseFrame(byte[] buffer, int length, KLineMode mode)
    {
        bool ok = KLineFrameParser.TryParse(buffer.AsSpan(0, length), mode, out var payload);
        return (ok, payload.ToArray());
    }

    // Helper builds an ECU→tester KWP response frame (target/source swapped vs request).
    private static byte[] BuildResponse(byte[] payload)
    {
        byte[] frame = new byte[4 + payload.Length + 1];
        frame[0] = 0x80;
        frame[1] = 0xF5; // target = tester
        frame[2] = 0xD5; // source = ECU
        frame[3] = (byte)payload.Length;
        payload.CopyTo(frame, 4);
        frame[^1] = KLineChecksum.Calculate(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }
}
