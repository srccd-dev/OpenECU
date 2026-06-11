using AwesomeAssertions;
using OpenEcu.Core.Security;
using Xunit;

namespace OpenEcu.Core.Tests.Security;

public class SagemSeedKeyTests
{
    // Vectors derived independently from the published master constant (see docs/SEEDKEY.md).
    [Theory]
    [InlineData(0x0000, 0x0000)]
    [InlineData(0x0001, 0x6789)]
    [InlineData(0x1234, 0xA9D4)]
    [InlineData(0xABCD, 0x6BB5)]
    [InlineData(0xFFFF, 0x9877)]
    public void ComputeKey_read_level_matches_known_vectors(int seed, int expected)
    {
        ushort key = SagemSeedKey.ComputeKey((ushort)seed, SecurityLevel.Read);
        key.Should().Be((ushort)expected);
    }
}
