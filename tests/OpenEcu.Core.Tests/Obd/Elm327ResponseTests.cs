using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class Elm327ResponseTests
{
    [Fact]
    public void Parses_hex_data_line()
    {
        Elm327Response.TryParse("4100BE1E9011\r", out byte[] bytes).Should().BeTrue();
        bytes.Should().Equal(0x41, 0x00, 0xBE, 0x1E, 0x90, 0x11);
    }

    [Fact]
    public void Strips_spaces_and_the_searching_line()
    {
        Elm327Response.TryParse("SEARCHING...\r41 0C 1A F8\r", out byte[] bytes).Should().BeTrue();
        bytes.Should().Equal(0x41, 0x0C, 0x1A, 0xF8);
    }

    [Fact]
    public void No_data_is_an_error()
    {
        Elm327Response.TryParse("NO DATA\r", out _).Should().BeFalse();
    }

    [Fact]
    public void Question_mark_and_unable_to_connect_are_errors()
    {
        Elm327Response.TryParse("?\r", out _).Should().BeFalse();
        Elm327Response.TryParse("UNABLE TO CONNECT\r", out _).Should().BeFalse();
    }
}
