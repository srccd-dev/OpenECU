using FluentAssertions;
using OpenEcu.Core.Protocol;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class KLineFrameBuilderTests
{
    [Fact]
    public void Builds_iso9141_request_with_length_in_format_byte()
    {
        byte[] frame = KLineFrameBuilder.BuildRequest(new byte[] { 0x81 }, KLineMode.Iso9141);
        frame.Should().Equal(0x81, 0xD5, 0xF5, 0x81, 0xCC);
    }

    [Fact]
    public void Builds_kwp2000_request_with_separate_length_byte()
    {
        byte[] frame = KLineFrameBuilder.BuildRequest(new byte[] { 0x81 }, KLineMode.Kwp2000);
        frame.Should().Equal(0x80, 0xD5, 0xF5, 0x01, 0x81, 0xCC);
    }

    [Fact]
    public void Iso9141_multibyte_payload_sets_format_byte_to_0x80_or_length()
    {
        byte[] frame = KLineFrameBuilder.BuildRequest(new byte[] { 0x10, 0x20, 0x30 }, KLineMode.Iso9141);
        // 0x80|3 = 0x83 ; checksum = 0x83+0xD5+0xF5+0x10+0x20+0x30 = 0x2AD -> 0xAD
        frame.Should().Equal(0x83, 0xD5, 0xF5, 0x10, 0x20, 0x30, 0xAD);
    }

    [Fact]
    public void Rejects_payload_longer_than_63_in_iso_mode()
    {
        var tooLong = new byte[64];
        var act = () => KLineFrameBuilder.BuildRequest(tooLong, KLineMode.Iso9141);
        act.Should().Throw<ArgumentException>();
    }
}
