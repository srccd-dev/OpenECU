using AwesomeAssertions;
using OpenEcu.Core.Protocol;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class KLineChecksumTests
{
    [Fact]
    public void Calculate_sums_bytes_modulo_256()
    {
        // 0x81 + 0xD5 + 0xF5 + 0x81 = 0x2CC -> low byte 0xCC
        byte[] data = { 0x81, 0xD5, 0xF5, 0x81 };
        KLineChecksum.Calculate(data).Should().Be(0xCC);
    }

    [Fact]
    public void Calculate_of_empty_is_zero()
    {
        KLineChecksum.Calculate(ReadOnlySpan<byte>.Empty).Should().Be(0x00);
    }

    [Fact]
    public void Calculate_wraps_past_256()
    {
        byte[] data = { 0xFF, 0x02 }; // 0x101 -> 0x01
        KLineChecksum.Calculate(data).Should().Be(0x01);
    }
}
