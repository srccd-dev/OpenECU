using AwesomeAssertions;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class IObdSessionTests
{
    [Fact]
    public void KLineObdSession_implements_IObdSession()
    {
        typeof(IObdSession).IsAssignableFrom(typeof(KLineObdSession)).Should().BeTrue();
    }
}
