using AwesomeAssertions;
using OpenEcu.App.Model;
using Xunit;

namespace OpenEcu.App.Tests;

public class MetricCatalogTests
{
    [Fact]
    public void Known_pid_has_display_metadata()
    {
        var d = MetricCatalog.For(0x0C); // RPM
        d.Name.Should().Be("Engine RPM");
        d.Unit.Should().Be("rpm");
        d.Min.Should().Be(0);
        d.Max.Should().Be(12000);
    }

    [Fact]
    public void Coolant_range_supports_below_zero()
    {
        var d = MetricCatalog.For(0x05);
        d.Min.Should().Be(-40);
        d.Max.Should().Be(150);
    }

    [Fact]
    public void Unknown_pid_returns_a_safe_fallback()
    {
        var d = MetricCatalog.For(0xAB);
        d.Name.Should().Be("PID AB");
        d.Min.Should().Be(0);
        d.Max.Should().Be(255);
        MetricCatalog.IsKnown(0xAB).Should().BeFalse();
        MetricCatalog.IsKnown(0x0C).Should().BeTrue();
    }
}
