using AwesomeAssertions;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;
using Xunit;

namespace OpenEcu.App.Tests;

public class DashboardViewModelTests
{
    private static async Task<LiveDataService> ConnectedService(params byte[] supported)
    {
        var ecu = new FakeObdSession();
        ecu.Supported.AddRange(supported);
        var svc = new LiveDataService(ecu);
        await svc.ConnectAsync();
        return svc;
    }

    [Fact]
    public async Task Heroes_are_the_layout_hero_pids_in_order()
    {
        var svc = await ConnectedService(0x0C, 0x05, 0x11);
        var vm = new DashboardViewModel(svc);

        vm.Heroes.Select(m => m.Pid).Should().Equal((byte)0x0C, (byte)0x05);
    }

    [Fact]
    public async Task Tiles_are_the_supported_non_hero_metrics()
    {
        var svc = await ConnectedService(0x0C, 0x05, 0x11, 0x0F);
        var vm = new DashboardViewModel(svc);

        vm.Tiles.Select(m => m.Pid).Should().Contain(new byte[] { 0x11, 0x0F });
        vm.Tiles.Select(m => m.Pid).Should().NotContain(new byte[] { 0x0C, 0x05 });
    }

    [Fact]
    public async Task Dtcs_passes_through_from_the_service()
    {
        var svc = await ConnectedService(0x0C);
        var vm = new DashboardViewModel(svc);
        vm.Dtcs.Should().BeSameAs(svc.Dtcs);
    }
}
