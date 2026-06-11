namespace OpenEcu.Core.Memory;

/// <summary>An addressable byte buffer over a read region, indexed by absolute address.</summary>
public sealed class MemoryImage
{
    private readonly byte[] _bytes;

    public MemoryImage(int baseAddress, byte[] bytes)
    {
        BaseAddress = baseAddress;
        _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
    }

    public int BaseAddress { get; }
    public int Length => _bytes.Length;

    public byte this[int address]
    {
        get
        {
            int offset = address - BaseAddress;
            if (offset < 0 || offset >= _bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(address),
                    $"Address 0x{address:X} outside image [0x{BaseAddress:X}, 0x{BaseAddress + _bytes.Length:X}).");
            return _bytes[offset];
        }
    }

    public ReadOnlySpan<byte> Slice(int address, int length)
    {
        int offset = address - BaseAddress;
        if (offset < 0 || length < 0 || offset + length > _bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(address),
                $"Slice [0x{address:X}, +{length}) outside image [0x{BaseAddress:X}, 0x{BaseAddress + _bytes.Length:X}).");
        return _bytes.AsSpan(offset, length);
    }
}
