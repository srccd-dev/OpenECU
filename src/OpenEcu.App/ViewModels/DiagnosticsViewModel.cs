using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenEcu.App.Services;

namespace OpenEcu.App.ViewModels;

/// <summary>The full live PID table + fault codes, with Mode 04 clear-codes.</summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly LiveDataService _live;

    public DiagnosticsViewModel(LiveDataService live) => _live = live;

    public ObservableCollection<MetricViewModel> Metrics => _live.Metrics;
    public IReadOnlyList<string> Dtcs => _live.Dtcs;

    public bool CanClearCodes => true;

    [RelayCommand]
    private Task ClearCodesAsync() => _live.ClearDtcsAsync();
}
