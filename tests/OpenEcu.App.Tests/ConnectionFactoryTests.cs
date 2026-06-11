using AwesomeAssertions;
using OpenEcu.App.Services;
using Xunit;

namespace OpenEcu.App.Tests;

public class ConnectionFactoryTests
{
    [Fact]
    public void Create_builds_a_connection_without_opening_the_port()
    {
        // Constructing the stack must not throw or open hardware — just wire it up.
        var conn = new ConnectionFactory().Create("COM_NONEXISTENT");
        conn.Service.Should().NotBeNull();
        conn.Log.Should().NotBeNull();
        conn.Log.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Create_builds_an_elm327_connection_without_opening()
    {
        var conn = new ConnectionFactory().Create("COM_NONEXISTENT", AdapterKind.Elm327);
        conn.Service.Should().NotBeNull();
        conn.Log.IsOpen.Should().BeFalse();
    }
}
