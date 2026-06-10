using OpenEcu.Core.Protocol;

namespace OpenEcu.Core.Adapters;

/// <summary>
/// Performs an ISO9141 5-baud slow init: bit-bangs an address byte on the break line,
/// one bit per bit-period. The line idles high (break off); a 1 bit drives it low.
/// </summary>
public sealed class KLineFiveBaudInitializer
{
    private readonly TimeSpan _bitPeriod;
    private readonly Func<TimeSpan, Task> _delay;

    /// <param name="bitPeriod">5-baud bit time; the original uses ~196 ms.</param>
    /// <param name="delay">Override the wait (for tests). Defaults to Task.Delay.</param>
    public KLineFiveBaudInitializer(TimeSpan? bitPeriod = null, Func<TimeSpan, Task>? delay = null)
    {
        _bitPeriod = bitPeriod ?? TimeSpan.FromMilliseconds(196);
        _delay = delay ?? Task.Delay;
    }

    public async Task InitializeAsync(IBreakLine line, byte address, CancellationToken ct = default)
    {
        bool[] states = FiveBaudInitPattern.BreakStatesFor(address);
        foreach (bool breakOn in states)
        {
            ct.ThrowIfCancellationRequested();
            await _delay(_bitPeriod);
            line.SetBreak(breakOn);
        }
        // The final (stop) bit is break-off, so the line is already idle-high here.
    }
}
