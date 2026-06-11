using AwesomeAssertions;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;
using OpenEcu.Core.Transport;
using Xunit;

namespace OpenEcu.App.Tests;

public class MainViewModelTests
{
    private sealed class FakeFactory : IConnectionFactory
    {
        public FakeObdSession Ecu { get; } = new();
        public LiveConnection Create(string portName, AdapterKind kind = AdapterKind.Cable)
        {
            var log = new LoggingTransport(new SimulatedTransport());
            return new LiveConnection(new LiveDataService(Ecu), log);
        }
    }

    private static MainViewModel New(FakeFactory f, params string[] ports) =>
        new(f, () => ports);

    [Fact]
    public void RefreshPorts_populates_and_selects_first()
    {
        var vm = New(new FakeFactory(), "COM3", "COM8");
        vm.AvailablePorts.Should().Equal("COM3", "COM8");
        vm.SelectedPort.Should().Be("COM3");
    }

    [Fact]
    public async Task Connect_then_disconnect_transitions_state()
    {
        var f = new FakeFactory();
        f.Ecu.Supported.AddRange(new byte[] { 0x0C, 0x05 });
        var vm = New(f, "COM8");

        await vm.ConnectCommand.ExecuteAsync(null);
        vm.State.Should().Be(ConnectionState.Connected);
        vm.Live.Should().NotBeNull();

        await vm.DisconnectCommand.ExecuteAsync(null);
        vm.State.Should().Be(ConnectionState.Disconnected);
    }

    [Fact]
    public async Task Connect_failure_sets_error_state()
    {
        var f = new FakeFactory { };
        f.Ecu.ThrowOnConnect = true;
        var vm = New(f, "COM8");

        await vm.ConnectCommand.ExecuteAsync(null);

        vm.State.Should().Be(ConnectionState.Error);
    }
}
