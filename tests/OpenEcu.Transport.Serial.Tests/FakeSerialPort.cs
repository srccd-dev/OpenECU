using OpenEcu.Transport.Serial;

namespace OpenEcu.Transport.Serial.Tests;

/// <summary>In-memory ISerialPort: records writes, replays scripted reads, tracks open state.</summary>
public sealed class FakeSerialPort : ISerialPort
{
    private readonly List<byte> _written = new();
    private readonly Queue<byte> _toRead = new();

    public bool IsOpen { get; private set; }
    public IReadOnlyList<byte> Written => _written;

    private readonly List<bool> _breakToggles = new();
    public IReadOnlyList<bool> BreakToggles => _breakToggles;
    public void SetBreak(bool on) => _breakToggles.Add(on);

    public void EnqueueRead(params byte[] data)
    {
        foreach (byte b in data) _toRead.Enqueue(b);
    }

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        _written.AddRange(data.ToArray());
        return Task.CompletedTask;
    }

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        int i = 0;
        while (i < buffer.Length && _toRead.Count > 0)
            buffer.Span[i++] = _toRead.Dequeue();
        return Task.FromResult(i);
    }

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        return ValueTask.CompletedTask;
    }
}
