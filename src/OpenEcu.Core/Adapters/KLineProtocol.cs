using System.IO;
using OpenEcu.Core.Protocol;
using OpenEcu.Core.Transport;

namespace OpenEcu.Core.Adapters;

/// <summary>
/// Tier-2 adapter for the "dumb" K-line cable: the host performs the ISO9141/KWP2000
/// message exchange itself over a raw byte-stream transport.
/// </summary>
public sealed class KLineProtocol : IEcuAdapter
{
    private readonly IEcuTransport _transport;
    private readonly KLineMode _mode;

    public KLineProtocol(IEcuTransport transport, KLineMode mode)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _mode = mode;
    }

    public bool IsConnected { get; private set; }

    public async Task<EcuResponse> RequestAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        byte[] request = KLineFrameBuilder.BuildRequest(payload.Span, _mode);
        await _transport.WriteAsync(request, ct);

        byte[] responseFrame = await KLineFrameReader.ReadFrameAsync(_transport, _mode, ct);
        return ParseResponse(responseFrame);
    }

    private EcuResponse ParseResponse(byte[] responseFrame)
    {
        if (!KLineFrameParser.TryParse(responseFrame, _mode, out var responsePayload))
            throw new InvalidDataException("ECU response failed checksum validation.");
        return EcuResponse.FromPayload(responsePayload);
    }

    public Task ConnectAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task DisconnectAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task TesterPresentAsync(CancellationToken ct = default) => throw new NotImplementedException();

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}
