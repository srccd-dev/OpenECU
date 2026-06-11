using AwesomeAssertions;
using OpenEcu.App.Model;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;
using Xunit;

namespace OpenEcu.App.Tests;

public class RacingDashboardViewModelTests
{
    private static async Task<LiveDataService> Connected(params byte[] supported)
    {
        var ecu = new FakeObdSession();
        ecu.Supported.AddRange(supported);
        var svc = new LiveDataService(ecu);
        await svc.ConnectAsync();
        return svc;
    }

    [Fact]
    public async Task Exposes_rpm_speed_gear_and_readouts()
    {
        var svc = await Connected(0x0C, 0x0D, 0x11, 0x05, 0x0E, 0x14);
        var vm = new RacingDashboardViewModel(svc);

        vm.Rpm!.Pid.Should().Be(0x0C);
        vm.Speed!.Pid.Should().Be(0x0D);
        vm.Gear.Should().Be("—");
        vm.Readouts.Select(m => m.Pid).Should().Equal((byte)0x11, (byte)0x05, (byte)0x0E, (byte)0x14);
    }

    [Fact]
    public async Task Uses_the_default_tach_config()
    {
        var svc = await Connected(0x0C);
        var vm = new RacingDashboardViewModel(svc);
        vm.Tach.Should().Be(TachConfig.Default);
    }
}
