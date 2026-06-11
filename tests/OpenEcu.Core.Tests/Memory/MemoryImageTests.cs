using AwesomeAssertions;
using OpenEcu.Core.Memory;
using Xunit;

namespace OpenEcu.Core.Tests.Memory;

public class MemoryImageTests
{
    [Fact]
    public void Indexer_returns_byte_at_absolute_address()
    {
        var image = new MemoryImage(0x1000, new byte[] { 0xAA, 0xBB, 0xCC });

        image.BaseAddress.Should().Be(0x1000);
        image.Length.Should().Be(3);
        image[0x1000].Should().Be((byte)0xAA);
        image[0x1002].Should().Be((byte)0xCC);
    }

    [Fact]
    public void Slice_returns_region_by_absolute_address()
    {
        var image = new MemoryImage(0x1000, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });

        image.Slice(0x1001, 2).ToArray().Should().Equal(new byte[] { 0xBB, 0xCC });
    }

    [Fact]
    public void Out_of_range_access_throws()
    {
        var image = new MemoryImage(0x1000, new byte[] { 0xAA });

        var below = () => image[0x0FFF];
        var above = () => image[0x1001];
        var slicePastEnd = () => image.Slice(0x1000, 2).ToArray();

        below.Should().Throw<ArgumentOutOfRangeException>();
        above.Should().Throw<ArgumentOutOfRangeException>();
        slicePastEnd.Should().Throw<ArgumentOutOfRangeException>();
    }
}
