namespace OpenEcu.Core.Protocol;

/// <summary>A normalized ECU response: either positive (with data) or negative (with an NRC).</summary>
public sealed class EcuResponse
{
    private EcuResponse(bool isPositive, byte serviceId, byte nrc, byte[] data)
    {
        IsPositive = isPositive;
        ServiceId = serviceId;
        NegativeResponseCode = nrc;
        Data = data;
    }

    /// <summary>True for a positive response, false for a negative (0x7F) response.</summary>
    public bool IsPositive { get; }

    /// <summary>
    /// For a positive response, the response SID (request SID + 0x40).
    /// For a negative response, the rejected request's SID.
    /// </summary>
    public byte ServiceId { get; }

    /// <summary>Negative response code; 0 for a positive response.</summary>
    public byte NegativeResponseCode { get; }

    /// <summary>Positive-response data after the response SID; empty for a negative response.</summary>
    public IReadOnlyList<byte> Data { get; }

    public static EcuResponse FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
            throw new ArgumentException("Response payload is empty.", nameof(payload));

        if (payload[0] == KwpServiceId.NegativeResponse)
        {
            if (payload.Length < 3)
                throw new ArgumentException("Negative response must be 0x7F, SID, NRC.", nameof(payload));
            return new EcuResponse(isPositive: false, serviceId: payload[1], nrc: payload[2], data: Array.Empty<byte>());
        }

        return new EcuResponse(isPositive: true, serviceId: payload[0], nrc: 0x00, data: payload[1..].ToArray());
    }
}
