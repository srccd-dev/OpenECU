using OpenEcu.App.Model;
using OpenEcu.App.Services;

namespace OpenEcu.App.ViewModels;

/// <summary>Composes the dashboard's hero gauges and tiles from the layout + the live metrics.</summary>
public sealed class DashboardViewModel
{
    private readonly LiveDataService _live;
    private readonly DashboardLayout _layout;

    public DashboardViewModel(LiveDataService live, DashboardLayout? layout = null)
    {
        _live = live;
        _layout = layout ?? DashboardLayout.Default;
    }

    public IReadOnlyList<MetricViewModel> Heroes =>
        _layout.HeroPids
            .Select(pid => _live.Metrics.FirstOrDefault(m => m.Pid == pid))
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();

    public IReadOnlyList<MetricViewModel> Tiles =>
        _live.Metrics.Where(m => !_layout.HeroPids.Contains(m.Pid)).ToList();

    public IReadOnlyList<string> Dtcs => _live.Dtcs;
}
