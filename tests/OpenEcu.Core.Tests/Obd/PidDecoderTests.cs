using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class PidDecoderTests
{
    [Fact]
    public void Decodes_coolant_temp_from_real_bytes()
    {
        var r = PidDecoder.Decode(0x05, new byte[] { 0x44 }); // 0x44 = 68 -> 28 C
        r.Name.Should().Be("Coolant temperature");
        r.Value.Should().Be(28);
        r.Unit.Should().Be("C");
    }

    [Fact]
    public void Decodes_rpm()
    {
        PidDecoder.Decode(0x0C, new byte[] { 0x00, 0x00 }).Value.Should().Be(0);
        PidDecoder.Decode(0x0C, new byte[] { 0x0B, 0xB8 }).Value.Should().Be(750); // (0x0BB8)/4
    }

    [Fact]
    public void Decodes_throttle_percent()
    {
        var r = PidDecoder.Decode(0x11, new byte[] { 0x1C }); // 28*100/255
        r.Value.Should().BeApproximately(10.98, 0.01);
        r.Unit.Should().Be("%");
    }

    [Fact]
    public void Decodes_intake_air_temp()
    {
        PidDecoder.Decode(0x0F, new byte[] { 0x49 }).Value.Should().Be(33); // 73-40
    }

    [Fact]
    public void Decodes_timing_advance()
    {
        PidDecoder.Decode(0x0E, new byte[] { 0x44 }).Value.Should().Be(-30); // 68/2-64
    }

    [Fact]
    public void Decodes_o2_sensor_voltage()
    {
        var r = PidDecoder.Decode(0x14, new byte[] { 0x5D, 0x80 }); // 0x5D=93 -> 0.465 V
        r.Value.Should().BeApproximately(0.465, 0.0001);
        r.Unit.Should().Be("V");
    }

    [Fact]
    public void Unknown_pid_returns_raw_with_null_value()
    {
        var r = PidDecoder.Decode(0x1C, new byte[] { 0x05 });
        r.Value.Should().BeNull();
        r.Raw.Should().Equal(0x05);
    }
}
