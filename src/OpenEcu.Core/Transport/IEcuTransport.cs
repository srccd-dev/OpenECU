namespace OpenEcu.Core.Transport;

/// <summary>
/// Raw byte-stream link to an ECU adapter (FTDI cable, serial/SPP, Bluetooth, ...).
/// Tier-1 of the two-tier transport/adapter model.
/// </summary>
public interface IEcuTransport : IAsyncDisposable
{
    bool IsOpen { get; }
    Task OpenAsync(CancellationToken ct = default);
    Task CloseAsync();
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>Reads up to buffer.Length bytes; returns the count actually read.</summary>
    Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);
}
