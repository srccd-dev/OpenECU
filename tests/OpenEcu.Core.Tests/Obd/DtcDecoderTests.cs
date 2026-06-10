using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class DtcDecoderTests
{
    [Fact]
    public void Decodes_the_real_stored_dtc()
    {
        // Mode 03 payload (after service id 0x43) captured from the bike.
        var codes = DtcDecoder.Decode(new byte[] { 0x15, 0x02, 0x00, 0x00, 0x00, 0x00 });
        codes.Should().Equal("P1502");
    }

    [Fact]
    public void Decodes_each_prefix()
    {
        DtcDecoder.Decode(new byte[] { 0x01, 0x33 }).Should().Equal("P0133");
        DtcDecoder.Decode(new byte[] { 0x41, 0x23 }).Should().Equal("C0123");
        DtcDecoder.Decode(new byte[] { 0x81, 0x45 }).Should().Equal("B0145");
        DtcDecoder.Decode(new byte[] { 0xC1, 0x67 }).Should().Equal("U0167");
    }

    [Fact]
    public void Skips_empty_pairs_and_handles_no_codes()
    {
        DtcDecoder.Decode(new byte[] { 0x00, 0x00, 0x00, 0x00 }).Should().BeEmpty();
    }
}
