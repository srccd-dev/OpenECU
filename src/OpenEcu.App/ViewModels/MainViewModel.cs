using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenEcu.App.Model;
using OpenEcu.App.Services;
using OpenEcu.Core.Transport;
using OpenEcu.Transport.Serial;

namespace OpenEcu.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IConnectionFactory _factory;
    private readonly Func<IReadOnlyList<string>> _portProvider;
    private LiveConnection? _connection;
    private CancellationTokenSource? _loopCts;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;

    public MainViewModel(IConnectionFactory factory, Func<IReadOnlyList<string>>? portProvider = null, string? settingsPath = null)
    {
        _factory = factory;
        _portProvider = portProvider ?? (() => SerialPortEnumerator.GetPortNames());
        _settingsPath = settingsPath ?? AppSettings.DefaultPath;
        _settings = AppSettings.Load(_settingsPath);
        _darkMode = _settings.DarkMode;
        _accent = _settings.Accent;
        _racingMode = _settings.RacingMode;
        RefreshPorts();
    }

    public ObservableCollection<string> AvailablePorts { get; } = new();

    [ObservableProperty] private string? _selectedPort;
    [ObservableProperty] private ConnectionState _state = ConnectionState.Disconnected;
    [ObservableProperty] private string _status = "Disconnected";
    [ObservableProperty] private bool _darkMode;
    [ObservableProperty] private string _accent = "teal";
    [ObservableProperty] private bool _racingMode;

    public IReadOnlyList<string> Accents => AppSettings.Accents;

    partial void OnDarkModeChanged(bool value) { _settings.DarkMode = value; _settings.Save(_settingsPath); }
    partial void OnAccentChanged(string value) { _settings.Accent = value; _settings.Save(_settingsPath); }
    partial void OnRacingModeChanged(bool value) { _settings.RacingMode = value; _settings.Save(_settingsPath); }

    /// <summary>The connected live data service (null until connected). Views bind metrics from it.</summary>
    public LiveDataService? Live => _connection?.Service;

    /// <summary>The logging transport for the console (null until connected).</summary>
    public LoggingTransport? Log => _connection?.Log;

    public void RefreshPorts()
    {
        AvailablePorts.Clear();
        foreach (string p in _portProvider()) AvailablePorts.Add(p);
        SelectedPort ??= AvailablePorts.FirstOrDefault();
    }

    [RelayCommand]
    private void RefreshPorts_() => RefreshPorts();

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrEmpty(SelectedPort)) return;
        State = ConnectionState.Connecting;
        Status = $"Connecting to {SelectedPort}…";
        try
        {
            _connection = _factory.Create(SelectedPort);
            await _connection.Log.OpenAsync();
            await _connection.Service.ConnectAsync();
            OnPropertyChanged(nameof(Live));
            OnPropertyChanged(nameof(Log));
            State = ConnectionState.Connected;
            Status = "Connected";
            _loopCts = new CancellationTokenSource();
            _ = _connection.Service.RunAsync(_loopCts.Token);
        }
        catch (Exception ex)
        {
            State = ConnectionState.Error;
            Status = $"Connect failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        _loopCts?.Cancel();
        if (_connection is not null)
        {
            try { await _connection.Log.CloseAsync(); await _connection.Service.DisposeAsync(); }
            catch { /* ignore on teardown */ }
        }
        _connection = null;
        OnPropertyChanged(nameof(Live));
        OnPropertyChanged(nameof(Log));
        State = ConnectionState.Disconnected;
        Status = "Disconnected";
    }
}
