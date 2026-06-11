using OpenEcu.Core.Obd;

namespace OpenEcu.Core.Security;

/// <summary>
/// Drives the Sagem SecurityAccess (0x27) seed-key handshake over an IObdRequestChannel:
/// request seed (27 03 02) -> compute key -> send key (27 03 02 KH KL) -> confirm granted.
/// A seed of 0 means the ECU is already unlocked. Throws SecurityAccessException on 0x7F.
/// </summary>
public sealed class SagemSecurityAccess
{
    private readonly IObdRequestChannel _channel;

    public SagemSecurityAccess(IObdRequestChannel channel)
        => _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task UnlockAsync(SecurityLevel level, CancellationToken ct = default)
    {
        ObdResponse seedResp = await _channel.RequestAsync(new byte[] { 0x27, 0x03, 0x02 }, ct);
        ThrowIfRejected(seedResp);
        if (seedResp.ServiceId != 0x67 || seedResp.Payload.Length < 4)
            throw new SecurityAccessException(0, $"Unexpected seed response: SID 0x{seedResp.ServiceId:X2}.");

        ushort seed = (ushort)((seedResp.Payload[2] << 8) | seedResp.Payload[3]);
        if (seed == 0) return; // already unlocked

        ushort key = SagemSeedKey.ComputeKey(seed, level);
        ObdResponse keyResp = await _channel.RequestAsync(
            new byte[] { 0x27, 0x03, 0x02, (byte)(key >> 8), (byte)(key & 0xFF) }, ct);
        ThrowIfRejected(keyResp);
        if (keyResp.ServiceId != 0x67)
            throw new SecurityAccessException(0, $"Key not accepted: SID 0x{keyResp.ServiceId:X2}.");
    }

    private static void ThrowIfRejected(ObdResponse resp)
    {
        if (resp.ServiceId != 0x7F || resp.Payload.Length < 2) return;
        byte nrc = resp.Payload[1];
        throw new SecurityAccessException(nrc, $"SecurityAccess rejected (NRC 0x{nrc:X2}: {NrcName(nrc)}).");
    }

    private static string NrcName(byte nrc) => nrc switch
    {
        0x35 => "invalidKey",
        0x36 => "exceededNumberOfAttempts",
        0x37 => "requiredTimeDelayNotExpired",
        _ => "unknown",
    };
}
