using AwesomeAssertions;
using OpenEcu.App.Model;
using Xunit;

namespace OpenEcu.App.Tests;

public class AccentPaletteTests
{
    [Fact]
    public void Known_accents_map_to_rgb()
    {
        AccentPalette.Rgb("teal").Should().Be((29, 158, 117));
        AccentPalette.Rgb("red").Should().Be((226, 75, 74));
    }

    [Fact]
    public void Unknown_accent_falls_back_to_teal()
    {
        AccentPalette.Rgb("chartreuse").Should().Be(AccentPalette.Rgb("teal"));
    }
}
