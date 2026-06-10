using OpenEcu.Core.Protocol;

namespace OpenEcu.Core.Adapters;

/// <summary>
/// Tier-2 adapter: a request/response conversation with an ECU over some transport.
/// Implementations: KLineProtocol (dumb cable) and, later, Elm327Adapter (smart adapters).
/// </summary>
public interface IEcuAdapter : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>Performs the protocol handshake (e.g. StartCommunication). Throws on failure.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Cleanly ends the session (e.g. StopCommunication).</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>Sends a service request payload and returns the ECU's response.</summary>
    Task<EcuResponse> RequestAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default);

    /// <summary>Sends a TesterPresent keep-alive.</summary>
    Task TesterPresentAsync(CancellationToken ct = default);
}
