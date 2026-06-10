using AwesomeAssertions;
using OpenEcu.App.Model;
using OpenEcu.App.ViewModels;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.App.Tests;

public class MetricViewModelTests
{
    private static MetricViewModel ForRpm() => new(MetricCatalog.For(0x0C));

    [Fact]
    public void Exposes_descriptor_metadata()
    {
        var vm = ForRpm();
        vm.Pid.Should().Be(0x0C);
        vm.Name.Should().Be("Engine RPM");
        vm.Unit.Should().Be("rpm");
        vm.Maximum.Should().Be(12000);
    }

    [Fact]
    public void Update_sets_value_display_and_clears_stale()
    {
        var vm = ForRpm();
        vm.Update(new PidReading(0x0C, "Engine RPM", 1080, "rpm", new byte[] { 0x10, 0xE0 }));

        vm.Value.Should().Be(1080);
        vm.IsStale.Should().BeFalse();
        vm.Display.Should().Be("1080 rpm");
        vm.Raw.Should().Equal(0x10, 0xE0);
    }

    [Fact]
    public void Null_value_shows_dash_and_marks_stale()
    {
        var vm = ForRpm();
        vm.Update(new PidReading(0x0C, "Engine RPM", null, "rpm", Array.Empty<byte>()));

        vm.Value.Should().BeNull();
        vm.IsStale.Should().BeTrue();
        vm.Display.Should().Be("—");
    }

    [Fact]
    public void Repeated_identical_readings_flag_static()
    {
        var vm = ForRpm();
        var reading = new OpenEcu.Core.Obd.PidReading(0x0C, "Engine RPM", 1000, "rpm", new byte[] { 0x0F, 0xA0 });

        for (int i = 0; i < 8; i++) vm.Update(reading);

        vm.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void A_changed_reading_clears_static()
    {
        var vm = ForRpm();
        var a = new OpenEcu.Core.Obd.PidReading(0x0C, "Engine RPM", 1000, "rpm", new byte[] { 0x0F, 0xA0 });
        for (int i = 0; i < 8; i++) vm.Update(a);
        vm.IsStatic.Should().BeTrue();

        vm.Update(new OpenEcu.Core.Obd.PidReading(0x0C, "Engine RPM", 1200, "rpm", new byte[] { 0x12, 0xC0 }));

        vm.IsStatic.Should().BeFalse();
    }
}
