namespace OpenEcu.Core.Protocol;

/// <summary>
/// Standard KWP2000 / ISO 14230 service identifiers used by the K-line handshake.
/// Public protocol constants (not derived from any proprietary source).
/// </summary>
public static class KwpServiceId
{
    public const byte StartCommunication = 0x81;
    public const byte StopCommunication = 0x82;
    public const byte TesterPresent = 0x3E;

    /// <summary>First byte of a negative response: 0x7F, &lt;requestSid&gt;, &lt;nrc&gt;.</summary>
    public const byte NegativeResponse = 0x7F;

    /// <summary>A positive response SID equals the request SID plus this offset.</summary>
    public const byte PositiveResponseOffset = 0x40;

    /// <summary>Expected positive-response SID for a given request SID.</summary>
    public static byte PositiveResponseFor(byte requestSid) => (byte)(requestSid + PositiveResponseOffset);
}
