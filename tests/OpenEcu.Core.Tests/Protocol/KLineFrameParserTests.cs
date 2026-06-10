using AwesomeAssertions;
using OpenEcu.Core.Protocol;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class KLineFrameParserTests
{
    [Fact]
    public void Parses_payload_from_valid_iso_frame()
    {
        // header(3) + payload(0x41,0x42) + checksum
        byte[] frame = { 0x82, 0xF5, 0xD5, 0x41, 0x42, Sum(0x82, 0xF5, 0xD5, 0x41, 0x42) };
        bool ok = KLineFrameParser.TryParse(frame, KLineMode.Iso9141, out var payload);
        ok.Should().BeTrue();
        payload.ToArray().Should().Equal(0x41, 0x42);
    }

    [Fact]
    public void Parses_payload_from_valid_kwp_frame()
    {
        byte[] frame = { 0x80, 0xF5, 0xD5, 0x02, 0x41, 0x42, Sum(0x80, 0xF5, 0xD5, 0x02, 0x41, 0x42) };
        bool ok = KLineFrameParser.TryParse(frame, KLineMode.Kwp2000, out var payload);
        ok.Should().BeTrue();
        payload.ToArray().Should().Equal(0x41, 0x42);
    }

    [Fact]
    public void Rejects_frame_with_bad_checksum()
    {
        byte[] frame = { 0x82, 0xF5, 0xD5, 0x41, 0x42, 0x00 };
        bool ok = KLineFrameParser.TryParse(frame, KLineMode.Iso9141, out _);
        ok.Should().BeFalse();
    }

    [Fact]
    public void Rejects_frame_too_short_to_contain_header_and_checksum()
    {
        byte[] frame = { 0x82, 0xF5 };
        bool ok = KLineFrameParser.TryParse(frame, KLineMode.Iso9141, out _);
        ok.Should().BeFalse();
    }

    private static byte Sum(params int[] bytes)
    {
        int s = 0;
        foreach (int b in bytes) s += b;
        return (byte)s;
    }
}
