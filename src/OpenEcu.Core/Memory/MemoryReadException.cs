namespace OpenEcu.Core.Memory;

/// <summary>Thrown when an ECU memory read fails (KWP negative response, or a malformed block).</summary>
public sealed class MemoryReadException : Exception
{
    public byte Nrc { get; }
    public int Address { get; }

    public MemoryReadException(byte nrc, int address, string message) : base(message)
    {
        Nrc = nrc;
        Address = address;
    }
}
