using FluentAssertions;
using OpenEcu.Transport.Serial;
using Xunit;

namespace OpenEcu.Transport.Serial.Tests;

public class SerialPortEnumeratorTests
{
    [Fact]
    public void GetPortNames_returns_a_non_null_array()
    {
        // We can't assert specific ports (machine-dependent), but it must never return null
        // or throw, and every entry must be a non-empty string.
        string[] ports = SerialPortEnumerator.GetPortNames();

        ports.Should().NotBeNull();
        ports.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p));
    }
}
