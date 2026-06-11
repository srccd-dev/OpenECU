using AwesomeAssertions;
using OpenEcu.App.Model;
using Xunit;

namespace OpenEcu.App.Tests;

public class TachConfigTests
{
    [Fact]
    public void Default_matches_the_955i()
    {
        TachConfig.Default.MaxRpm.Should().Be(11000);
        TachConfig.Default.RedlineRpm.Should().Be(9500);
    }

    [Fact]
    public void Is_configurable_per_model()
    {
        var cfg = new TachConfig(MaxRpm: 14000, RedlineRpm: 12500);
        cfg.MaxRpm.Should().Be(14000);
        cfg.RedlineRpm.Should().Be(12500);
    }
}
