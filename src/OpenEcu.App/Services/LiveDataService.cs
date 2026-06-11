using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenEcu.App.Model;
using OpenEcu.App.ViewModels;
using OpenEcu.Core.Obd;

namespace OpenEcu.App.Services;

public enum ConnectionState { Disconnected, Connecting, Connected, Error }

/// <summary>
/// Owns an IObdSession, connects, and runs a weighted polling loop that keeps the metric
/// view-models live. Hero PIDs are polled every cycle; the rest are interleaved one per cycle.
/// DTCs refresh on a fixed cadence. UI-agnostic: callers marshal updates to their UI thread.
/// </summary>
public sealed partial class LiveDataService : ObservableObject, IAsyncDisposable
{
    private readonly IObdSession _session;
    private readonly DashboardLayout _layout;
    private readonly TimeSpan _dtcInterval;
    private readonly Dictionary<byte, MetricViewModel> _byPid = new();
    private int _tileCursor;
    private DateTime _lastDtc = DateTime.MinValue;

    public LiveDataService(IObdSession session, DashboardLayout? layout = null, TimeSpan? dtcInterval = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _layout = layout ?? DashboardLayout.Default;
        _dtcInterval = dtcInterval ?? TimeSpan.FromSeconds(5);
    }

    public ObservableCollection<MetricViewModel> Metrics { get; } = new();

    [ObservableProperty] private ConnectionState _state = ConnectionState.Disconnected;
    [ObservableProperty] private IReadOnlyList<string> _dtcs = Array.Empty<string>();
    [ObservableProperty] private DateTime _lastUpdate;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        State = ConnectionState.Connecting;
        try
        {
            await _session.ConnectAsync(ct);
            IReadOnlyList<byte> supported = await _session.ReadSupportedPidsAsync(ct);

            Metrics.Clear();
            _byPid.Clear();
            _tileCursor = 0;
            foreach (byte pid in supported)
            {
                if (pid is 0x20 or 0x40) continue; // bitmask chain PIDs, not data
                var vm = new MetricViewModel(MetricCatalog.For(pid));
                Metrics.Add(vm);
                _byPid[pid] = vm;
            }
            State = ConnectionState.Connected;
        }
        catch
        {
            State = ConnectionState.Error;
            throw;
        }
    }

    /// <summary>Runs one weighted poll cycle. Call repeatedly (see RunAsync).</summary>
    public async Task PollOnceAsync(CancellationToken ct = default)
    {
        foreach (byte pid in _layout.HeroPids)
            await PollPidAsync(pid, ct);

        if (NextTilePid() is byte tile)
            await PollPidAsync(tile, ct);

        if (DateTime.UtcNow - _lastDtc >= _dtcInterval)
        {
            try
            {
                Dtcs = await _session.ReadDtcsAsync(ct);
                _lastDtc = DateTime.UtcNow;
            }
            catch { /* transient; retry next cadence */ }
        }

        RefreshStaticFlags();
        LastUpdate = DateTime.UtcNow;
    }

    /// <summary>Clears stored DTCs on the ECU, then re-reads them.</summary>
    public async Task ClearDtcsAsync(CancellationToken ct = default)
    {
        await _session.ClearDtcsAsync(ct);
        Dtcs = await _session.ReadDtcsAsync(ct);
    }

    /// <summary>Continuous loop until cancelled. Callers run this on a background task.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await PollOnceAsync(ct);
            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private byte? NextTilePid()
    {
        var heroes = _layout.HeroPids;
        var tiles = _byPid.Keys.Where(p => !heroes.Contains(p)).OrderBy(p => p).ToList();
        if (tiles.Count == 0) return null;
        byte pid = tiles[_tileCursor % tiles.Count];
        _tileCursor++;
        return pid;
    }

    private async Task PollPidAsync(byte pid, CancellationToken ct)
    {
        if (!_byPid.TryGetValue(pid, out var vm)) return;
        try { vm.Update(await _session.ReadPidAsync(pid, ct)); }
        catch { vm.IsStale = true; }
    }

    private const double EngineRunningRpm = 400;

    // Compute the static flag once at the end of the cycle so it never flickers mid-poll.
    // A PID is "static" only if it has repeated AND the engine is running (else everything
    // looks static at idle/off, which is meaningless).
    private void RefreshStaticFlags()
    {
        bool running = _byPid.TryGetValue(0x0C, out var rpm) && rpm.Value is double v && v >= EngineRunningRpm;
        foreach (MetricViewModel m in Metrics)
            m.IsStatic = m.Repeated && running;
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
