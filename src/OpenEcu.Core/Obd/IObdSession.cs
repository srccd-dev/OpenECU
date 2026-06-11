namespace OpenEcu.Core.Obd;

/// <summary>A read-only OBD diagnostic session (implemented by KLineObdSession).</summary>
public interface IObdSession : IAsyncDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task<IReadOnlyList<byte>> ReadSupportedPidsAsync(CancellationToken ct = default);
    Task<PidReading> ReadPidAsync(byte pid, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ReadDtcsAsync(CancellationToken ct = default);

    /// <summary>Clears stored diagnostic trouble codes (OBD-II Mode 04).</summary>
    Task ClearDtcsAsync(CancellationToken ct = default);
}
