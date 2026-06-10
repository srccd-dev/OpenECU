using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using OpenEcu.App.Model;
using OpenEcu.App.Services;
using OpenEcu.App.ViewModels;

namespace OpenEcu.Desktop.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private ConsoleViewModel? _console;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => OnVmAttached(DataContext as MainViewModel);
        OnVmAttached(DataContext as MainViewModel);
    }

    private void OnVmAttached(MainViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = vm;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVmChanged;
        ApplyTheme();
        ApplyAccent();
        UpdateViews();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Live): UpdateViews(); break;
            case nameof(MainViewModel.DarkMode): ApplyTheme(); break;
            case nameof(MainViewModel.Accent): ApplyAccent(); break;
        }
    }

    private void ApplyTheme()
    {
        if (Application.Current is { } app && _vm is not null)
            app.RequestedThemeVariant = _vm.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private void ApplyAccent()
    {
        if (Application.Current is not { } app || _vm is null) return;
        var (r, g, b) = AccentPalette.Rgb(_vm.Accent);
        app.Resources["AppAccentBrush"] = new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    private void UpdateViews()
    {
        var dash = this.FindControl<ContentControl>("DashboardHost");
        var diag = this.FindControl<ContentControl>("DiagnosticsHost");
        var con = this.FindControl<ContentControl>("ConsoleHost");

        if (_vm?.Live is LiveDataService live)
        {
            if (dash is not null) dash.Content = new DashboardView { DataContext = new DashboardViewModel(live) };
            if (diag is not null) diag.Content = new DiagnosticsView { DataContext = new DiagnosticsViewModel(live) };

            _console = new ConsoleViewModel();
            if (_vm.Log is { } log)
            {
                log.BytesWritten += _console.OnTx;
                log.BytesRead += _console.OnRx;
            }
            if (con is not null) con.Content = new ConsoleView { DataContext = _console };
        }
        else
        {
            if (dash is not null) dash.Content = NotConnected();
            if (diag is not null) diag.Content = NotConnected();
            if (con is not null) con.Content = NotConnected();
        }
    }

    private static TextBlock NotConnected() => new()
    {
        Text = "Not connected — pick a port and Connect.",
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        Opacity = 0.6,
    };
}
