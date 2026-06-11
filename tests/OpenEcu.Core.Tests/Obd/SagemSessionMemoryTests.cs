using AwesomeAssertions;
using OpenEcu.Core.Memory;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Obd;

public class SagemSessionMemoryTests
{
    private static Task NoDelay(TimeSpan _) => Task.CompletedTask;

    [Fact]
    public async Task ReadMemoryAsync_returns_image_with_base_address_and_bytes()
    {
        // Request 68 6A F1 23 00 10 00 04 00 FA -> payload "230010000400".
        // Response 48 6B D1 63 11 22 33 44 <ck>; ck = (0x48+0x6B+0xD1+0x63+0x11+0x22+0x33+0x44) & 0xFF = 0x91.
        var ecu = new FakeEcu(new()
        {
            ["230010000400"] = new byte[] { 0x48, 0x6B, 0xD1, 0x63, 0x11, 0x22, 0x33, 0x44, 0x91 },
        }, connected: true);
        await ecu.OpenAsync();
        await using var sagem = new SagemSession(ecu, ecu, delay: NoDelay);

        MemoryImage image = await sagem.ReadMemoryAsync(0x001000, 4);

        image.BaseAddress.Should().Be(0x001000);
        image.Length.Should().Be(4);
        image.Slice(0x001000, 4).ToArray().Should().Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 });
    }
}
