using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class ObdMessageTests
{
    [Fact]
    public void BuildRequest_prepends_68_6A_F1_and_appends_checksum()
    {
        // Real request captured from the bike for Mode 01 PID 0C.
        byte[] frame = ObdMessage.BuildRequest(new byte[] { 0x01, 0x0C });
        frame.Should().Equal(0x68, 0x6A, 0xF1, 0x01, 0x0C, 0xD0);
    }

    [Fact]
    public void TryParseResponse_extracts_service_and_payload()
    {
        // Real response: 48 6B D1 | 41 | 0C 00 00 | D1
        byte[] frame = { 0x48, 0x6B, 0xD1, 0x41, 0x0C, 0x00, 0x00, 0xD1 };
        ObdMessage.TryParseResponse(frame, out ObdResponse resp).Should().BeTrue();
        resp.ServiceId.Should().Be(0x41);
        resp.Payload.Should().Equal(0x0C, 0x00, 0x00);
    }

    [Fact]
    public void TryParseResponse_rejects_bad_checksum()
    {
        byte[] frame = { 0x48, 0x6B, 0xD1, 0x41, 0x0C, 0x00, 0x00, 0x00 };
        ObdMessage.TryParseResponse(frame, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseResponse_rejects_too_short()
    {
        byte[] frame = { 0x48, 0x6B, 0xD1 };
        ObdMessage.TryParseResponse(frame, out _).Should().BeFalse();
    }
}
