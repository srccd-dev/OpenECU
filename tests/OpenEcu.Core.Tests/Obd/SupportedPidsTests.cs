using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class SupportedPidsTests
{
    [Fact]
    public void Parses_real_bitmask_from_the_bike()
    {
        // PID 00 response data BE 1E 90 11 advertises PIDs 01-20.
        var pids = SupportedPids.Parse(0x00, new byte[] { 0xBE, 0x1E, 0x90, 0x11 });
        pids.Should().Equal(0x01, 0x03, 0x04, 0x05, 0x06, 0x07, 0x0C, 0x0D, 0x0E, 0x0F, 0x11, 0x14, 0x1C, 0x20);
    }

    [Fact]
    public void Applies_base_offset_for_the_21_40_range()
    {
        // 21-40 bitmask 00 00 00 01 advertises only PID 40 (the next-range chain bit).
        var pids = SupportedPids.Parse(0x20, new byte[] { 0x00, 0x00, 0x00, 0x01 });
        pids.Should().Equal(0x40);
    }

    [Fact]
    public void Empty_range_yields_nothing()
    {
        var pids = SupportedPids.Parse(0x40, new byte[] { 0x00, 0x00, 0x00, 0x00 });
        pids.Should().BeEmpty();
    }

    [Fact]
    public void Throws_when_bitmask_is_not_four_bytes()
    {
        var act = () => SupportedPids.Parse(0x00, new byte[] { 0x00 });
        act.Should().Throw<ArgumentException>();
    }
}
