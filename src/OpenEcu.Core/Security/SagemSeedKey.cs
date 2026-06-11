namespace OpenEcu.Core.Security;

/// <summary>
/// Pure Sagem MC1000 seed-to-key transform. No I/O, no state. Implemented from the
/// published seed-key standard (see docs/SEEDKEY.md): key = (seed * multiplier) mod 65536,
/// where the multiplier is derived from a published 64-bit master constant and the level.
/// </summary>
public static class SagemSeedKey
{
    // Published master constant. KeyR/KeyW are derived from it once.
    private const ulong Master = 0x9A5F944B3A59454BUL;
    private static readonly ushort KeyR;

    static SagemSeedKey()
    {
        uint low32 = (uint)(Master & 0xFFFFFFFF);
        uint high32 = (uint)(Master >> 32);
        uint keyw0 = high32 ^ low32;
        KeyR = (ushort)((keyw0 >> 16) & 0xFFFF); // 0xA006
    }

    /// <summary>Computes the unlock key for an ECU-supplied seed at the given level.</summary>
    public static ushort ComputeKey(ushort seed, SecurityLevel level) => level switch
    {
        SecurityLevel.Read => (ushort)((seed * (KeyR ^ 51087)) & 0xFFFF), // multiplier 0x6789
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unsupported security level."),
    };
}
