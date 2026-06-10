using FluentAssertions;
using OpenEcu.Core.Protocol;
using Xunit;

namespace OpenEcu.Core.Tests.Protocol;

public class EcuResponseTests
{
    [Fact]
    public void Positive_response_exposes_sid_and_payload()
    {
        // StartCommunication positive: 0xC1 + two key bytes
        byte[] payload = { 0xC1, 0xEA, 0x8F };
        var r = EcuResponse.FromPayload(payload);

        r.IsPositive.Should().BeTrue();
        r.ServiceId.Should().Be(0xC1);
        r.NegativeResponseCode.Should().Be(0x00);
        r.Data.Should().Equal(0xEA, 0x8F); // payload after the response SID
    }

    [Fact]
    public void Negative_response_exposes_request_sid_and_nrc()
    {
        // 0x7F, <request sid>, <nrc>
        byte[] payload = { 0x7F, 0x81, 0x10 };
        var r = EcuResponse.FromPayload(payload);

        r.IsPositive.Should().BeFalse();
        r.ServiceId.Should().Be(0x81);              // the request that was rejected
        r.NegativeResponseCode.Should().Be(0x10);
        r.Data.Should().BeEmpty();
    }

    [Fact]
    public void Empty_payload_throws()
    {
        var act = () => EcuResponse.FromPayload(ReadOnlySpan<byte>.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Malformed_negative_response_throws()
    {
        byte[] payload = { 0x7F, 0x81 }; // missing NRC byte
        var act = () => EcuResponse.FromPayload(payload);
        act.Should().Throw<ArgumentException>();
    }
}
