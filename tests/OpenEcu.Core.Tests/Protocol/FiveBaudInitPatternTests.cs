using AwesomeAssertions;
using OpenEcu.Core.Protocol;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class FiveBaudInitPatternTests
{
    // pattern = address*4 + 1025. Each of the 11 entries is the BREAK-ON state for that
    // bit-period: break is ON (line low) when the pattern bit is 0 (per ISOFT.SetBreak).
    // 0x33: pattern = 51*4 + 1025 = 1229; LSB-first bits 1,0,1,1,0,0,1,1,0,0,1
    //       break-on (bit==0):       F,T,F,F,T,T,F,F,T,T,F
    [Fact]
    public void Address_0x33_produces_the_expected_break_on_states()
    {
        bool[] states = FiveBaudInitPattern.BreakStatesFor(0x33);
        states.Should().Equal(false, true, false, false, true, true, false, false, true, true, false);
    }

    // 0xD5: pattern = 213*4 + 1025 = 1877; LSB-first bits 1,0,1,0,1,0,1,0,1,1,1
    //       break-on (bit==0):        F,T,F,T,F,T,F,T,F,F,F
    [Fact]
    public void Address_0xD5_produces_the_expected_break_on_states()
    {
        bool[] states = FiveBaudInitPattern.BreakStatesFor(0xD5);
        states.Should().Equal(false, true, false, true, false, true, false, true, false, false, false);
    }

    [Fact]
    public void First_state_is_idle_high_and_second_is_the_low_start_bit()
    {
        // The frame leads in HIGH (break off) then drops LOW (break on) for the start bit.
        bool[] states = FiveBaudInitPattern.BreakStatesFor(0x33);
        states[0].Should().BeFalse(); // lead-in: line high
        states[1].Should().BeTrue();  // start bit: line low
    }

    [Fact]
    public void Always_returns_eleven_states()
    {
        FiveBaudInitPattern.BreakStatesFor(0x00).Should().HaveCount(11);
        FiveBaudInitPattern.BreakStatesFor(0xFF).Should().HaveCount(11);
    }
}
