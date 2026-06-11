using AwesomeAssertions;
using OpenEcu.Core.Obd;
using OpenEcu.Core.Security;
using Xunit;

namespace OpenEcu.Core.Tests.Security;

public class SagemSecurityAccessTests
{
    // A scripted request channel: records what was sent, replays canned responses in order.
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
    public async Task UnlockAsync_sends_seed_request_then_computed_key()
    {
        var channel = new ScriptedChannel(
            new ObdResponse(0x67, new byte[] { 0x03, 0x02, 0x12, 0x34 }), // seed 0x1234
            new ObdResponse(0x67, new byte[] { 0x03, 0x02 }));            // granted (no seed)
        var sa = new SagemSecurityAccess(channel);

        await sa.UnlockAsync(SecurityLevel.Read);

        channel.Sent.Should().HaveCount(2);
        channel.Sent[0].Should().Equal(new byte[] { 0x27, 0x03, 0x02 });
        channel.Sent[1].Should().Equal(new byte[] { 0x27, 0x03, 0x02, 0xA9, 0xD4 }); // key for 0x1234
    }

    [Fact]
    public async Task UnlockAsync_skips_key_when_already_unlocked()
    {
        var channel = new ScriptedChannel(
            new ObdResponse(0x67, new byte[] { 0x03, 0x02, 0x00, 0x00 })); // seed 0 = already unlocked
        var sa = new SagemSecurityAccess(channel);

        await sa.UnlockAsync(SecurityLevel.Read);

        channel.Sent.Should().ContainSingle(); // no key sent
    }

    [Fact]
    public async Task UnlockAsync_throws_on_negative_response()
    {
        var channel = new ScriptedChannel(
            new ObdResponse(0x67, new byte[] { 0x03, 0x02, 0x12, 0x34 }),
            new ObdResponse(0x7F, new byte[] { 0x27, 0x35 })); // invalidKey
        var sa = new SagemSecurityAccess(channel);

        Func<Task> act = () => sa.UnlockAsync(SecurityLevel.Read);

        await act.Should().ThrowAsync<SecurityAccessException>()
            .Where(e => e.Nrc == 0x35);
    }
}
