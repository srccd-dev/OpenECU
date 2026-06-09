using System.Collections.Generic;

namespace OpenEcu.Core.Transport;

/// <summary>In-memory transport for unit tests: records writes, replays scripted reads.</summary>
public sealed class SimulatedTransport : IEcuTransport
{
    private readonly List<byte> _written = new();
    private readonly Queue<byte> _responses = new();

    public bool IsOpen { get; private set; }

    /// <summary>Bytes written by the code under test, in order.</summary>
    public IReadOnlyList<byte> Written => _written;

    /// <summary>Queue bytes that subsequent ReadAsync calls will return.</summary>
    public void EnqueueResponse(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            _responses.Enqueue(b);
    }

    public Task OpenAsync(CancellationToken ct = default)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        IsOpen = false;
        return Task.CompletedTask;
    }

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        EnsureOpen();
        _written.AddRange(data.ToArray());
        return Task.CompletedTask;
    }

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        EnsureOpen();
        int i = 0;
        while (i < buffer.Length && _responses.Count > 0)
            buffer.Span[i++] = _responses.Dequeue();
        return Task.FromResult(i);
    }

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
            throw new InvalidOperationException("Transport is not open.");
    }
}
