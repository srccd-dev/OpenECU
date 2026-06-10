using AwesomeAssertions;
using OpenEcu.App.Model;
using Xunit;

namespace OpenEcu.App.Tests;

public class DashboardLayoutTests
{
    [Fact]
    public void Default_heroes_are_rpm_then_coolant()
    {
        DashboardLayout.Default.HeroPids.Should().Equal((byte)0x0C, (byte)0x05);
    }

    [Fact]
    public void Default_tiles_cover_the_other_common_sensors()
    {
        DashboardLayout.Default.TilePids.Should()
            .Contain(new byte[] { 0x11, 0x0F, 0x04, 0x0E, 0x14, 0x0D });
    }

    [Fact]
    public void Heroes_and_tiles_do_not_overlap()
    {
        var layout = DashboardLayout.Default;
        layout.HeroPids.Should().NotIntersectWith(layout.TilePids);
    }
}
