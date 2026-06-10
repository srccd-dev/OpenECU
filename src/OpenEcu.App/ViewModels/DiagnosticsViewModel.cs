using System.Collections.ObjectModel;
using OpenEcu.App.Services;

namespace OpenEcu.App.ViewModels;

/// <summary>The full live PID table + fault codes. Clear-codes (Mode 04) is a v2 stub.</summary>
public sealed class DiagnosticsViewModel
{
    private readonly LiveDataService _live;

    public DiagnosticsViewModel(LiveDataService live) => _live = live;

    public ObservableCollection<MetricViewModel> Metrics => _live.Metrics;
    public IReadOnlyList<string> Dtcs => _live.Dtcs;

    /// <summary>Mode 04 clear-DTCs is not implemented in v1 (read-only).</summary>
    public bool CanClearCodes => false;
}
