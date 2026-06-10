namespace OpenEcu.Core.Protocol;

/// <summary>Thrown when the transport stream ends before a complete frame was read.</summary>
public sealed class IncompleteFrameException : Exception
{
    public IncompleteFrameException(string message) : base(message) { }
}
