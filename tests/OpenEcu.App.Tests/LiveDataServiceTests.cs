using AwesomeAssertions;
using OpenEcu.App.Model;
using OpenEcu.App.Services;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.App.Tests;

public class LiveDataServiceTests
{
    private static PidReading Rpm(int v) => new(0x0C, "Engine RPM", v, "rpm", new byte[] { 0, 0 });

    [Fact]
    public async Task ConnectAsync_builds_metrics_for_supported_known_pids()
    {
        var fake = new FakeObdSession();
        fake.Supported.AddRange(new byte[] { 0x0C, 0x05, 0x11, 0x20 }); // 0x20 is a chain bit
        var svc = new LiveDataService(fake);

        await svc.ConnectAsync();

        svc.State.Should().Be(ConnectionState.Connected);
        svc.Metrics.Select(m => m.Pid).Should().Equal((byte)0x0C, (byte)0x05, (byte)0x11); // no 0x20
    }

    [Fact]
    public async Task ConnectAsync_failure_sets_error_state_and_rethrows()
    {
        var fake = new FakeObdSession { ThrowOnConnect = true };
        var svc = new LiveDataService(fake);

        var act = async () => await svc.ConnectAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        svc.State.Should().Be(ConnectionState.Error);
    }

    [Fact]
    public async Task PollOnceAsync_updates_metric_values_and_heartbeat()
    {
        var fake = new FakeObdSession();
        fake.Supported.AddRange(new byte[] { 0x0C, 0x05 });
        fake.Readings[0x0C] = Rpm(1080);
        var svc = new LiveDataService(fake);
        await svc.ConnectAsync();

        await svc.PollOnceAsync();

        svc.Metrics.First(m => m.Pid == 0x0C).Value.Should().Be(1080);
        svc.LastUpdate.Should().BeAfter(DateTime.MinValue);
    }

    [Fact]
    public async Task Hero_pids_are_polled_every_cycle()
    {
        var fake = new FakeObdSession();
        fake.Supported.AddRange(new byte[] { 0x0C, 0x05, 0x11, 0x0F, 0x04 }); // 2 heroes + 3 tiles
        var svc = new LiveDataService(fake);
        await svc.ConnectAsync();

        // Heroes (RPM 0x0C, coolant 0x05) get a fresh value on each of two cycles even though
        // only one tile is polled per cycle.
        fake.Readings[0x0C] = Rpm(1000);
        await svc.PollOnceAsync();
        fake.Readings[0x0C] = Rpm(2000);
        await svc.PollOnceAsync();

        svc.Metrics.First(m => m.Pid == 0x0C).Value.Should().Be(2000);
    }

    [Fact]
    public async Task A_failing_pid_is_marked_stale_without_stalling_others()
    {
        var fake = new FakeObdSession();
        fake.Supported.AddRange(new byte[] { 0x0C, 0x05 });
        fake.Readings[0x0C] = Rpm(900);
        fake.FailingPids.Add(0x05);
        var svc = new LiveDataService(fake);
        await svc.ConnectAsync();

        await svc.PollOnceAsync(); // must not throw

        svc.Metrics.First(m => m.Pid == 0x05).IsStale.Should().BeTrue();
        svc.Metrics.First(m => m.Pid == 0x0C).Value.Should().Be(900);
    }

    [Fact]
    public async Task Dtcs_refresh_on_first_cycle_then_respect_the_interval()
    {
        var fake = new FakeObdSession();
        fake.Supported.Add(0x0C);
        fake.Dtcs = new[] { "P1502" };
        var svc = new LiveDataService(fake, dtcInterval: TimeSpan.FromSeconds(30));
        await svc.ConnectAsync();

        await svc.PollOnceAsync(); // first cycle: reads DTCs
        await svc.PollOnceAsync(); // within interval: does NOT read again

        fake.DtcCalls.Should().Be(1);
        svc.Dtcs.Should().Equal("P1502");
    }

    [Fact]
    public async Task Static_is_suppressed_when_engine_is_off()
    {
        var fake = new FakeObdSession();
        fake.Supported.AddRange(new byte[] { 0x0C, 0x05 });
        fake.Readings[0x0C] = new PidReading(0x0C, "Engine RPM", 0, "rpm", new byte[] { 0, 0 });
        fake.Readings[0x05] = new PidReading(0x05, "Coolant", 80, "C", new byte[] { 0x78 });
        var svc = new LiveDataService(fake);
        await svc.ConnectAsync();

        for (int i = 0; i < 10; i++) await svc.PollOnceAsync();

        svc.Metrics.First(m => m.Pid == 0x05).IsStatic.Should().BeFalse();
    }

    [Fact]
    public async Task Static_applies_when_engine_running()
    {
        var fake = new FakeObdSession();
        fake.Supported.AddRange(new byte[] { 0x0C, 0x05 });
        fake.Readings[0x0C] = new PidReading(0x0C, "Engine RPM", 1200, "rpm", new byte[] { 0x12, 0xC0 });
        fake.Readings[0x05] = new PidReading(0x05, "Coolant", 80, "C", new byte[] { 0x78 }); // never changes
        var svc = new LiveDataService(fake);
        await svc.ConnectAsync();

        for (int i = 0; i < 10; i++) await svc.PollOnceAsync();

        svc.Metrics.First(m => m.Pid == 0x05).IsStatic.Should().BeTrue();
    }

    [Fact]
    public async Task ClearDtcsAsync_clears_on_the_ecu_and_refreshes()
    {
        var fake = new FakeObdSession();
        fake.Supported.Add(0x0C);
        fake.Dtcs = new[] { "P1502" };
        var svc = new LiveDataService(fake);
        await svc.ConnectAsync();
        await svc.PollOnceAsync();
        svc.Dtcs.Should().Equal("P1502");

        await svc.ClearDtcsAsync();

        fake.ClearCalls.Should().Be(1);
        svc.Dtcs.Should().BeEmpty();
    }
}
