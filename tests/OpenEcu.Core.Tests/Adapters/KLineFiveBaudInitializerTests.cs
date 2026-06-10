using AwesomeAssertions;
using OpenEcu.Core.Adapters;
using OpenEcu.Core.Protocol;
using Xunit;

namespace OpenEcu.Core.Tests.Adapters;

public class KLineFiveBaudInitializerTests
{
    private sealed class RecordingBreakLine : IBreakLine
    {
        public List<bool> Toggles { get; } = new();
        public void SetBreak(bool on) => Toggles.Add(on);
    }

    [Fact]
    public async Task Drives_the_line_with_the_exact_break_pattern_for_0x33()
    {
        var line = new RecordingBreakLine();
        var init = new KLineFiveBaudInitializer(delay: _ => Task.CompletedTask);

        await init.InitializeAsync(line, 0x33);

        line.Toggles.Should().Equal(FiveBaudInitPattern.BreakStatesFor(0x33));
    }

    [Fact]
    public async Task Waits_one_bit_period_per_bit()
    {
        var line = new RecordingBreakLine();
        int delays = 0;
        var init = new KLineFiveBaudInitializer(delay: _ => { delays++; return Task.CompletedTask; });

        await init.InitializeAsync(line, 0x33);

        delays.Should().Be(11); // one wait per bit-period
    }

    [Fact]
    public async Task Ends_with_the_line_idle_high()
    {
        var line = new RecordingBreakLine();
        var init = new KLineFiveBaudInitializer(delay: _ => Task.CompletedTask);

        await init.InitializeAsync(line, 0x33);

        line.Toggles.Last().Should().BeFalse(); // stop bit: break off, line high
    }
}
