using AwesomeAssertions;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;
using Xunit;

namespace OpenEcu.App.Tests;

public class DiagnosticsViewModelTests
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
    public async Task Exposes_all_metrics_and_dtcs()
    {
        var svc = await Connected(0x0C, 0x05, 0x11);
        var vm = new DiagnosticsViewModel(svc);

        vm.Metrics.Select(m => m.Pid).Should().Equal((byte)0x0C, (byte)0x05, (byte)0x11);
        vm.Dtcs.Should().BeSameAs(svc.Dtcs);
    }

    [Fact]
    public async Task Clear_codes_is_disabled_in_v1()
    {
        var svc = await Connected(0x0C);
        var vm = new DiagnosticsViewModel(svc);
        vm.CanClearCodes.Should().BeFalse();
    }
}
