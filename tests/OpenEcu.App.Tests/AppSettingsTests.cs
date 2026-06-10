using AwesomeAssertions;
using OpenEcu.App.Model;
using Xunit;

namespace OpenEcu.App.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_are_light_theme_and_teal_accent()
    {
        var s = new AppSettings();
        s.DarkMode.Should().BeFalse();
        s.Accent.Should().Be("teal");
    }

    [Fact]
    public void Round_trips_through_a_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"openecu-settings-{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings { DarkMode = true, Accent = "red" }.Save(path);
            var loaded = AppSettings.Load(path);
            loaded.DarkMode.Should().BeTrue();
            loaded.Accent.Should().Be("red");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Load_returns_defaults_when_file_is_missing()
    {
        var s = AppSettings.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
        s.DarkMode.Should().BeFalse();
        s.Accent.Should().Be("teal");
    }
}
