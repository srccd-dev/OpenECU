namespace OpenEcu.Core.Adapters;

/// <summary>Thrown when the ECU handshake does not complete successfully.</summary>
public sealed class EcuConnectionException : Exception
{
    public EcuConnectionException(string message) : base(message) { }
}
