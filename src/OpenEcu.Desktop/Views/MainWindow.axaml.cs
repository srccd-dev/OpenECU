using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;

namespace OpenEcu.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => OnDataContextChanged(DataContext);
    }

    private MainViewModel? _vm;

    private void OnDataContextChanged(object? dc)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = dc as MainViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        UpdateContent();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Live)) UpdateContent();
    }

    private void UpdateContent()
    {
        var host = this.FindControl<ContentControl>("Host");
        if (host is null) return;
        host.Content = _vm?.Live is LiveDataService live
            ? new DashboardView { DataContext = new DashboardViewModel(live) }
            : null;
    }
}
