namespace OpenEcu.Core.Obd;

/// <summary>A request/response channel for raw OBD/KWP service calls (payload in, parsed response out).</summary>
public interface IObdRequestChannel
{
    Task<ObdResponse> RequestAsync(byte[] payload, CancellationToken ct = default);
}
