namespace OpenEcu.Core.Protocol;

/// <summary>Framing variant for a K-line message.</summary>
public enum KLineMode
{
    /// <summary>Length encoded in the low bits of the format byte (0x80 | len).</summary>
    Iso9141,
    /// <summary>Format byte fixed at 0x80, with a separate length byte after the header.</summary>
    Kwp2000
}
