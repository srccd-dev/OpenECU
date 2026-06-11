using OpenEcu.App.Model;
using OpenEcu.App.Services;

namespace OpenEcu.App.ViewModels;

/// <summary>The racing-mode skin: RPM tach + speed + (n/a) gear + a few race readouts.</summary>
public sealed class RacingDashboardViewModel
{
    private static readonly byte[] ReadoutPids = { 0x11, 0x05, 0x0E, 0x14 }; // throttle, coolant, timing, O2

    private readonly LiveDataService _live;

    public RacingDashboardViewModel(LiveDataService live, TachConfig? tach = null)
    {
        _live = live;
        Tach = tach ?? TachConfig.Default;
    }

    public TachConfig Tach { get; }

    public MetricViewModel? Rpm => Find(0x0C);
    public MetricViewModel? Speed => Find(0x0D);

    /// <summary>OBD-II doesn't expose gear on this bike; shown greyed.</summary>
    public string Gear => "—";

    public IReadOnlyList<MetricViewModel> Readouts =>
        ReadoutPids.Select(Find).Where(m => m is not null).Select(m => m!).ToList();

    private MetricViewModel? Find(byte pid) => _live.Metrics.FirstOrDefault(m => m.Pid == pid);
}
