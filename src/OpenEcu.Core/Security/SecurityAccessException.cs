namespace OpenEcu.Core.Security;

/// <summary>Thrown when the ECU rejects SecurityAccess (KWP negative response 0x7F). Carries the NRC byte.</summary>
public sealed class SecurityAccessException : Exception
{
    public byte Nrc { get; }

    public SecurityAccessException(byte nrc, string message) : base(message) => Nrc = nrc;
}
