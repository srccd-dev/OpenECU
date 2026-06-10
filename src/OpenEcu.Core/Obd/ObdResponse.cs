namespace OpenEcu.Core.Obd;

/// <summary>A parsed OBD-II response: the response service id and the bytes after it.</summary>
public sealed record ObdResponse(byte ServiceId, byte[] Payload);
