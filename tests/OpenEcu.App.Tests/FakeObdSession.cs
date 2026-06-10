using OpenEcu.Core.Obd;

namespace OpenEcu.App.Tests;

/// <summary>In-memory IObdSession for testing LiveDataService.</summary>
public sealed class FakeObdSession : IObdSession
{
    public bool IsConnected { get; private set; }
    public bool ThrowOnConnect { get; set; }
    public List<byte> Supported { get; } = new();
    public Dictionary<byte, PidReading> Readings { get; } = new();
    public HashSet<byte> FailingPids { get; } = new();
    public IReadOnlyList<string> Dtcs { get; set; } = Array.Empty<string>();
    public int DtcCalls { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        if (ThrowOnConnect) throw new InvalidOperationException("connect failed");
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<byte>> ReadSupportedPidsAsync(CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<byte>)Supported);

    public Task<PidReading> ReadPidAsync(byte pid, CancellationToken ct = default)
    {
        if (FailingPids.Contains(pid))
            throw new IOException($"PID {pid:X2} read failed");
        var reading = Readings.TryGetValue(pid, out var r)
            ? r
            : new PidReading(pid, $"PID {pid:X2}", 0, "", Array.Empty<byte>());
        return Task.FromResult(reading);
    }

    public Task<IReadOnlyList<string>> ReadDtcsAsync(CancellationToken ct = default)
    {
        DtcCalls++;
        return Task.FromResult(Dtcs);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
