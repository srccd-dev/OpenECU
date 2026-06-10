using AwesomeAssertions;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.App.Tests;

public class MainViewModelSettingsTests
{
    private sealed class NullFactory : IConnectionFactory
    {
        public LiveConnection Create(string portName) =>
            new(new LiveDataService(new FakeObdSession()), new LoggingTransport(new SimulatedTransport()));
    }

    [Fact]
    public void Defaults_load_light_and_teal()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oe-{Guid.NewGuid():N}.json");
        var vm = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
        vm.DarkMode.Should().BeFalse();
        vm.Accent.Should().Be("teal");
    }

    [Fact]
    public void Changing_theme_and_accent_persists_and_reloads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oe-{Guid.NewGuid():N}.json");
        try
        {
            var vm = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
            vm.DarkMode = true;
            vm.Accent = "red";

            var reloaded = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
            reloaded.DarkMode.Should().BeTrue();
            reloaded.Accent.Should().Be("red");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
