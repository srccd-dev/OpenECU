namespace OpenEcu.Core.Protocol;

/// <summary>Additive mod-256 checksum used by the K-line frame format.</summary>
public static class KLineChecksum
{
    public static byte Calculate(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        foreach (byte b in data)
            sum += b;
        return (byte)sum;
    }
}
