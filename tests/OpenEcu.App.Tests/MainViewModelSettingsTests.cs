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
        public LiveConnection Create(string portName, AdapterKind kind = AdapterKind.Cable) =>
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

    [Fact]
    public void Racing_mode_defaults_off_and_persists()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oe-{Guid.NewGuid():N}.json");
        try
        {
            var vm = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
            vm.RacingMode.Should().BeFalse();

            vm.RacingMode = true;
            var reloaded = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
            reloaded.RacingMode.Should().BeTrue();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Adapter_defaults_to_cable_and_persists()
    {
        string path = Path.Combine(Path.GetTempPath(), $"oe-{Guid.NewGuid():N}.json");
        try
        {
            var vm = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
            vm.SelectedAdapter.Should().Be(AdapterKind.Cable);

            vm.SelectedAdapter = AdapterKind.Elm327;
            var reloaded = new MainViewModel(new NullFactory(), () => Array.Empty<string>(), path);
            reloaded.SelectedAdapter.Should().Be(AdapterKind.Elm327);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
