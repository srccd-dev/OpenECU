using AwesomeAssertions;
using OpenEcu.Core.Memory;
using OpenEcu.Core.Obd;
using Xunit;

namespace OpenEcu.Core.Tests.Memory;

public class SagemMemoryReaderTests
{
    // Scripted request channel: records sent payloads, replays canned responses in order.
    private sealed class ScriptedChannel : IObdRequestChannel
    {
        private readonly Queue<ObdResponse> _responses;
        public List<byte[]> Sent { get; } = new();
        public ScriptedChannel(params ObdResponse[] responses) => _responses = new(responses);

        public Task<ObdResponse> RequestAsync(byte[] payload, CancellationToken ct = default)
        {
            Sent.Add(payload);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    [Fact]
    public async Task ReadMemoryAsync_single_block_sends_0x23_and_returns_payload()
    {
        var channel = new ScriptedChannel(
            new ObdResponse(0x63, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }));
        var reader = new SagemMemoryReader(channel);

        byte[] data = await reader.ReadMemoryAsync(0x123456, 4, blockSize: 32);

        data.Should().Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        channel.Sent.Should().ContainSingle();
        channel.Sent[0].Should().Equal(new byte[] { 0x23, 0x12, 0x34, 0x56, 0x04, 0x00 });
    }

    [Fact]
    public async Task ReadMemoryAsync_multi_block_increments_address_and_assembles()
    {
        var channel = new ScriptedChannel(
            new ObdResponse(0x63, new byte[] { 0x01, 0x02 }),  // block 0 @ 0x1000, len 2
            new ObdResponse(0x63, new byte[] { 0x03 }));        // block 1 @ 0x1002, len 1 (short final)
        var reader = new SagemMemoryReader(channel);

        byte[] data = await reader.ReadMemoryAsync(0x1000, 3, blockSize: 2);

        data.Should().Equal(new byte[] { 0x01, 0x02, 0x03 });
        channel.Sent.Should().HaveCount(2);
        channel.Sent[0].Should().Equal(new byte[] { 0x23, 0x00, 0x10, 0x00, 0x02, 0x00 });
        channel.Sent[1].Should().Equal(new byte[] { 0x23, 0x00, 0x10, 0x02, 0x01, 0x00 });
    }

    [Fact]
    public async Task ReadMemoryAsync_throws_on_negative_response()
    {
        var channel = new ScriptedChannel(
            new ObdResponse(0x7F, new byte[] { 0x23, 0x31 })); // requestOutOfRange
        var reader = new SagemMemoryReader(channel);

        Func<Task> act = () => reader.ReadMemoryAsync(0x9999, 16, blockSize: 32);

        await act.Should().ThrowAsync<MemoryReadException>()
            .Where(e => e.Nrc == 0x31 && e.Address == 0x9999);
    }
}
